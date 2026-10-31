using Fallery.Models;

namespace Fallery.Services;

public static class FolderAddress
{
    public static string Resolve(string text, string? currentFolder)
    {
        if (text.Contains('\0')) throw new ArgumentException("路径包含无效字符。");
        string path = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请输入文件夹路径。");
        if (path.Length == 2 && char.IsAsciiLetter(path[0]) && path[1] == ':') path += "\\";
        if (path.Contains("://", StringComparison.Ordinal) || path.IndexOfAny(['*', '?', '"']) >= 0 || path.IndexOf(':', Math.Min(2, path.Length)) >= 0)
            throw new ArgumentException("请输入本地或网络文件夹路径。");
        if (!Path.IsPathFullyQualified(path))
        {
            if (currentFolder is null || (path.Length >= 2 && path[1] == ':'))
                throw new ArgumentException("请输入完整路径，例如 E:\\图片。");
            path = Path.GetFullPath(path, currentFolder);
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static List<FolderEntry> GetChildren(string? folder, CancellationToken ct = default, bool showHidden = false)
    {
        ct.ThrowIfCancellationRequested();
        if (folder is null) return FolderScanner.GetRoots(ct);
        var result = new List<FolderEntry>();
        var skip = FileAttributes.System | (showHidden ? 0 : FileAttributes.Hidden);
        var options = new EnumerationOptions { AttributesToSkip = skip, IgnoreInaccessible = false };
        foreach (var directory in new DirectoryInfo(folder).EnumerateDirectories("*", options))
        {
            ct.ThrowIfCancellationRequested();
            result.Add(new(directory.FullName, directory.Name));
        }
        if (showHidden)
        {
            var visible = FolderScanner.ParallelTest(result.Select(e => e.Path).ToList(), FolderScanner.IsVisibleHiddenEntry, ct);
            result = result.Where((_, i) => visible[i]).ToList();
        }
        result.Sort((a, b) => FolderScanner.CompareNames(a.Name, b.Name));
        ct.ThrowIfCancellationRequested();
        return result;
    }
}
