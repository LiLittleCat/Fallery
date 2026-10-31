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
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    /// <summary>当前目录（不递归）下的图片和视频，按资源管理器的自然顺序排列。</summary>
    public static List<MediaItem> Scan(string folder, CancellationToken ct = default)
    {
        var items = new List<MediaItem>();
        foreach (var path in Directory.EnumerateFiles(folder, "*", TopLevelOnly))
        {
            ct.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(path);
            if (ImageExtensions.Contains(ext)) items.Add(new MediaItem(path, MediaKind.Image));
            else if (VideoExtensions.Contains(ext)) items.Add(new MediaItem(path, MediaKind.Video));
        }
        items.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));
        return items;
    }

    public static List<string> GetSubfolders(string folder)
    {
        try
        {
            var dirs = Directory.EnumerateDirectories(folder, "*", TopLevelOnly).ToList();
            dirs.Sort((a, b) => StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
            return dirs;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    public static bool HasSubfolders(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder, "*", TopLevelOnly).Any();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    // 资源管理器同款排序：img2 排在 img10 前面
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);
}
