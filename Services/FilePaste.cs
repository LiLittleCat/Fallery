using Windows.Storage;
using FileAttributes = System.IO.FileAttributes;

namespace Fallery.Services;

public sealed record FilePasteResult(IReadOnlyList<string> Copied, IReadOnlyList<string> Errors);

public static class FilePaste
{
    public static async Task<FilePasteResult> CopyAsync(IReadOnlyList<string> sources, string destination, CancellationToken ct)
    {
        var copied = new List<string>();
        var errors = new List<string>();
        destination = Path.GetFullPath(destination);
        await Task.Run(() => RejectLinkedParents(destination), ct);
        var target = await StorageFolder.GetFolderFromPathAsync(destination).AsTask(ct);
        foreach (string source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string path = Path.GetFullPath(source);
                bool folder = await Task.Run(() =>
                {
                    RejectLinkedParents(path);
                    bool isFolder = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
                    if (isFolder && IsSameOrChild(destination, path))
                        throw new IOException("不能把文件夹粘贴到自身或它的子文件夹中。");
                    return isFolder;
                }, ct);
                if (folder)
                {
                    var original = await StorageFolder.GetFolderFromPathAsync(path).AsTask(ct);
                    var created = await target.CreateFolderAsync(original.Name, CreationCollisionOption.GenerateUniqueName).AsTask(ct);
                    await CopyContentsAsync(path, created, ct);
                    copied.Add(created.Path);
                }
                else
                {
                    var original = await StorageFile.GetFileFromPathAsync(path).AsTask(ct);
                    var created = await original.CopyAsync(target, original.Name, NameCollisionOption.GenerateUniqueName).AsTask(ct);
                    copied.Add(created.Path);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or System.Runtime.InteropServices.COMException)
            { errors.Add($"{Path.GetFileName(source)}：{ex.Message}"); }
        }
        return new(copied, errors);
    }

    private static async Task CopyContentsAsync(string source, StorageFolder destination, CancellationToken ct)
    {
        // 联接和符号链接不能递归跟随，否则可能循环或复制到用户未选择的目录。
        var entries = await Task.Run(() => Directory.GetFileSystemEntries(source), ct);
        foreach (string path in entries)
        {
            ct.ThrowIfCancellationRequested();
            var attributes = await Task.Run(() => File.GetAttributes(path), ct);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("文件夹包含联接或符号链接，已停止复制；已复制的部分保留。");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                var child = await destination.CreateFolderAsync(Path.GetFileName(path), CreationCollisionOption.GenerateUniqueName).AsTask(ct);
                await CopyContentsAsync(path, child, ct);
            }
            else
            {
                var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct);
                await file.CopyAsync(destination, file.Name, NameCollisionOption.GenerateUniqueName).AsTask(ct);
            }
        }
    }

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
                throw new IOException("暂不支持通过联接或符号链接复制粘贴，请打开实际文件夹后重试。");
    }
}
