namespace Fallery.Services;

public static class FolderNavigator
{
    public static string? FindAdjacent(string current, bool forward, CancellationToken ct = default, string? boundary = null)
    {
        current = Normalize(current);
        boundary = Normalize(boundary ?? Path.GetPathRoot(current)!);
        if (!IsWithin(current, boundary)) throw new ArgumentException("当前目录不在遍历范围中。", nameof(current));
        var childrenCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        int visited = 0;

        List<string> Children(string path)
        {
            ct.ThrowIfCancellationRequested();
            if (childrenCache.TryGetValue(path, out var cached)) return cached;
            if (++visited > 10_000) throw new IOException("已检查较多目录，请打开更接近目标的目录后重试。");
            var children = new List<string>();
            try
            {
                // 联接和符号链接可能回到祖先目录，顺序浏览不穿过它们。
                foreach (var child in Directory.EnumerateDirectories(path, "*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
                }))
                {
                    ct.ThrowIfCancellationRequested();
                    children.Add(Normalize(child));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            children.Sort((a, b) => FolderScanner.CompareNames(Path.GetFileName(a), Path.GetFileName(b)));
            childrenCache.Add(path, children);
            return children;
        }

        string? Next(string path)
        {
            if (Children(path).FirstOrDefault() is { } child) return child;
            while (!string.Equals(path, boundary, StringComparison.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var parent = Path.GetDirectoryName(path);
                if (parent is null || !IsWithin(parent, boundary)) return null;
                if (Children(parent).FirstOrDefault(p => FolderScanner.CompareNames(Path.GetFileName(p), Path.GetFileName(path)) > 0) is { } sibling)
                    return sibling;
                path = parent;
            }
            return null;
        }

        string? Previous(string path)
        {
            if (string.Equals(path, boundary, StringComparison.OrdinalIgnoreCase)) return null;
            var parent = Path.GetDirectoryName(path);
            if (parent is null || !IsWithin(parent, boundary)) return null;
            var sibling = Children(parent).LastOrDefault(p => FolderScanner.CompareNames(Path.GetFileName(p), Path.GetFileName(path)) < 0);
            if (sibling is null) return parent;
            while (Children(sibling).LastOrDefault() is { } child) sibling = child;
            return sibling;
        }

        for (string? candidate = forward ? Next(current) : Previous(current); candidate is not null;
             candidate = forward ? Next(candidate) : Previous(candidate))
        {
            ct.ThrowIfCancellationRequested();
            if (FolderScanner.HasMedia(candidate, ct)) return candidate;
        }
        ct.ThrowIfCancellationRequested();
        return null;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool IsWithin(string path, string boundary) => string.Equals(path, boundary, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.EndsInDirectorySeparator(boundary) ? boundary : boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
