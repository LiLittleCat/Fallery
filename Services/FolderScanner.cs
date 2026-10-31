using System.Runtime.InteropServices;
using Fallery.Models;

namespace Fallery.Services;

public static class FolderScanner
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff",
        ".heic", ".heif", ".avif", ".jxr", ".ico",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".3gp",
    };

    private static readonly EnumerationOptions TopLevelOnly = new()
    {
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    /// <summary>当前目录（不递归）下的图片和视频，按资源管理器的自然顺序排列。</summary>
    public static List<MediaFile> Scan(string folder, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var items = new List<MediaFile>();
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", TopLevelOnly))
        {
            ct.ThrowIfCancellationRequested();
            if (GetMediaKind(file.FullName) is { } kind)
                items.Add(new MediaFile(file.FullName, kind) { Version = FileVersion.FromInfo(file) });
        }
        items.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));
        ct.ThrowIfCancellationRequested();
        return items;
    }

    public static List<MediaFile> ScanEntries(string folder, CancellationToken ct = default)
    {
        var files = Scan(folder, ct);
        var entries = GetChildFolders(folder, ct)
            .Where(child => ContainsMediaRecursive(child.Path, ct))
            .Select(child => new MediaFile(child.Path, MediaKind.Folder)
            {
                FolderModifiedTicks = GetFolderModifiedTicks(child.Path),
            }).ToList();
        entries.AddRange(files);
        ct.ThrowIfCancellationRequested();
        return entries;
    }

    public static List<MediaFile> FindFolderPreviews(string folder, CancellationToken ct = default)
        => FindFolderPreviews(folder, 4, ct);

    public static bool ContainsMediaRecursive(string folder, CancellationToken ct = default)
        => FindFolderPreviews(folder, 1, ct).Count > 0;

    private static List<MediaFile> FindFolderPreviews(string folder, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var previews = new List<MediaFile>();
        try
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return previews;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return previews; }
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.TryPop(out var current))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // 先取当前层，再按子目录自然顺序查找；找到足够候选就停止，不解码整棵树。
                foreach (var file in new DirectoryInfo(current).EnumerateFiles("*", TopLevelOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    if (GetMediaKind(file.FullName) is { } kind)
                        previews.Add(new(file.FullName, kind) { Version = FileVersion.FromInfo(file) });
                    if (previews.Count == limit) { ct.ThrowIfCancellationRequested(); return previews; }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
            try
            {
                // 跳过联接和符号链接，避免回到祖先目录或跨到其他磁盘无限遍历。
                var directories = new List<string>();
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
                };
                foreach (var directory in Directory.EnumerateDirectories(current, "*", options))
                {
                    ct.ThrowIfCancellationRequested();
                    directories.Add(directory);
                }
                directories.Sort((a, b) => CompareNames(Path.GetFileName(a), Path.GetFileName(b)));
                for (int i = directories.Count - 1; i >= 0; i--) pending.Push(directories[i]);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        }
        ct.ThrowIfCancellationRequested();
        return previews;
    }

    private static MediaKind? GetMediaKind(string path)
    {
        var extension = Path.GetExtension(path);
        if (ImageExtensions.Contains(extension)) return MediaKind.Image;
        if (VideoExtensions.Contains(extension)) return MediaKind.Video;
        return null;
    }

    public static bool HasMedia(string folder, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", TopLevelOnly))
            {
                ct.ThrowIfCancellationRequested();
                if (GetMediaKind(path) is not null) return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return false;
    }

    private static long? GetFolderModifiedTicks(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            return directory.Exists ? directory.LastWriteTimeUtc.Ticks : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static int CompareNames(string left, string right) => StrCmpLogicalW(left, right);

    public static List<FolderEntry> GetRoots(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var roots = new List<FolderEntry>();
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (Directory.Exists(pictures)) roots.Add(new(pictures, "图片"));
        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!drive.IsReady) continue;
                var label = drive.VolumeLabel;
                roots.Add(new(drive.RootDirectory.FullName, string.IsNullOrEmpty(label)
                    ? drive.Name : $"{label} ({drive.Name.TrimEnd('\\')})"));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // 枚举期间可移动磁盘可能已拔出。
            }
        }
        return roots;
    }

    public static List<string> GetSubfolders(string folder, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var dirs = new List<string>();
            foreach (var path in Directory.EnumerateDirectories(folder, "*", TopLevelOnly))
            {
                ct.ThrowIfCancellationRequested();
                dirs.Add(path);
            }
            dirs.Sort((a, b) => StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
            ct.ThrowIfCancellationRequested();
            return dirs;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    public static List<FolderChild> GetChildFolders(string folder, CancellationToken ct = default)
    {
        var children = new List<FolderChild>();
        foreach (var path in GetSubfolders(folder, ct))
        {
            ct.ThrowIfCancellationRequested();
            // 能列出名称不代表能访问目录；失败节点不应留下一个无法打开的入口。
            if (TryGetHasSubfolders(path, out var hasChildren)) children.Add(new(path, hasChildren));
        }
        ct.ThrowIfCancellationRequested();
        return children;
    }

    public static bool HasSubfolders(string folder) => TryGetHasSubfolders(folder, out var result) && result;

    private static bool TryGetHasSubfolders(string folder, out bool hasChildren)
    {
        try
        {
            hasChildren = Directory.EnumerateDirectories(folder, "*", TopLevelOnly).Any();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            hasChildren = false;
            return false;
        }
    }

    // 资源管理器同款排序：img2 排在 img10 前面
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);
}
