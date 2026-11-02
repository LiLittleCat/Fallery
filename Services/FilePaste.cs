using Windows.Storage;
using FileAttributes = System.IO.FileAttributes;

namespace Fallery.Services;

public enum FileConflictChoice { Skip, KeepBoth, Replace, Cancel }
public sealed record FileConflict(string Source, string Target, bool SourceFolder, bool TargetFolder);
public sealed record FilePasteResult(IReadOnlyList<string> Copied, IReadOnlyList<string> Errors)
{
    public int Skipped { get; init; }
    public bool Canceled { get; init; }
}

public static class FilePaste
{
    public static Task<FilePasteResult> CopyAsync(IReadOnlyList<string> sources, string destination, CancellationToken ct) =>
        TransferAsync(sources, destination, false, _ => Task.FromResult(FileConflictChoice.KeepBoth), ct);

    public static async Task<FilePasteResult> TransferAsync(IReadOnlyList<string> sources, string destination, bool move,
        Func<FileConflict, Task<FileConflictChoice>> resolve, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var copied = new List<string>();
        var errors = new List<string>();
        int skipped = 0;
        bool canceled = false;
        destination = Path.GetFullPath(destination);
        await Task.Run(() => RejectLinkedParents(destination), ct);
        var target = await StorageFolder.GetFolderFromPathAsync(destination);

        async Task<(string? Path, bool Complete)> Transfer(string path, StorageFolder into)
        {
            ct.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) is null)
                throw new IOException(AppText.Get("CannotCopyOrMoveADriveRoot"));
            bool folder = await Task.Run(() =>
            {
                RejectLinkedParents(path);
                bool isFolder = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
                if (isFolder && IsSameOrChild(into.Path, path))
                    throw new IOException(AppText.Get("CannotPasteAFolderIntoItselfOrOneOf"));
                return isFolder;
            }, ct);
            string name = Path.GetFileName(path);
            string targetPath = Path.Combine(into.Path, name);
            bool same = string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase);
            if (move && same) { skipped++; return (null, false); }
            var existing = await into.TryGetItemAsync(name);
            var choice = FileConflictChoice.KeepBoth;
            if (existing is not null && !same)
            {
                choice = await resolve(new(path, existing.Path, folder, existing is StorageFolder));
                if (choice == FileConflictChoice.Cancel) throw new OperationCanceledException();
                if (choice == FileConflictChoice.Skip) { skipped++; return (null, false); }
                if (choice == FileConflictChoice.Replace && folder != (existing is StorageFolder))
                    throw new IOException(AppText.Get("AFileCannotReplaceAFolderOrViceVersa"));
                await Task.Run(() => RejectLinkedParents(existing.Path), ct);
            }
            ct.ThrowIfCancellationRequested();
            if (!folder)
            {
                var original = await StorageFile.GetFileFromPathAsync(path);
                var collision = existing is null ? NameCollisionOption.FailIfExists
                    : choice == FileConflictChoice.Replace ? NameCollisionOption.ReplaceExisting : NameCollisionOption.GenerateUniqueName;
                // 等待原生操作完成后再处理取消，避免后台仍在移动而界面已报告结束。
                if (move)
                {
                    await original.MoveAsync(into, name, collision);
                    return (original.Path, true);
                }
                var created = await original.CopyAsync(into, name, collision);
                return (created.Path, true);
            }

            var destinationFolder = existing is StorageFolder existingFolder && choice == FileConflictChoice.Replace
                ? existingFolder : await into.CreateFolderAsync(name, existing is null ? CreationCollisionOption.FailIfExists : CreationCollisionOption.GenerateUniqueName);
            bool complete = true;
            var children = await Task.Run(() => Directory.GetFileSystemEntries(path), ct);
            foreach (string child in children)
            {
                try { var result = await Transfer(child, destinationFolder); complete &= result.Complete; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (IsFileError(ex)) { errors.Add($"{child}：{ex.Message}"); complete = false; }
            }
            if (move && complete)
            {
                // 只删除空目录。跳过、失败、取消或复制期间新加入的内容不能被递归删除。
                await Task.Run(() => Directory.Delete(path, recursive: false));
            }
            return (destinationFolder.Path, complete);
        }

        foreach (string source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var result = await Transfer(source, target);
                if (result.Path is not null) copied.Add(result.Path);
            }
            catch (OperationCanceledException) { canceled = true; break; }
            catch (Exception ex) when (IsFileError(ex)) { errors.Add($"{Path.GetFileName(source)}：{ex.Message}"); }
        }
        return new(copied, errors) { Skipped = skipped, Canceled = canceled };
    }

    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException
        or System.Runtime.InteropServices.COMException;

    private static bool IsSameOrChild(string path, string parent)
    {
        parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return path.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectLinkedParents(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(AppText.Get("CopyingThroughJunctionsOrSymbolicLinksIsNotSupported"));
    }
}
