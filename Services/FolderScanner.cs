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

    // 与资源管理器的“隐藏的项目”一致：系统受保护项（回收站、卷信息等）仍然跳过。
    private static readonly EnumerationOptions TopLevelWithHidden = new()
    {
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.System,
    };

    // 本应用只看图片和视频：版本库、依赖和工具链缓存里的图片多是图标资源，目录却极深，
    // 递归查找媒体、A/D 遍历和文件夹封面都不进入。只收录不会和相册重名的名称。
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".jj",
        "node_modules", "bower_components", ".npm", ".pnpm-store", ".yarn", ".bun", ".nvm", ".deno",
        ".next", ".nuxt", ".turbo", ".parcel-cache", ".svelte-kit", ".angular", ".expo",
        ".cargo", ".rustup", ".gradle", ".m2", ".ivy2", ".sbt", ".sdkman", ".konan",
        "__pycache__", ".venv", "venv", "site-packages", ".tox", ".nox", ".mypy_cache", ".pytest_cache", ".ruff_cache",
        ".conda", "miniconda3", "anaconda3", ".pyenv", ".rbenv", ".gem", ".pub-cache", ".dart_tool",
        ".nuget", ".dotnet", ".vs", ".idea", ".vscode", ".vscode-server", ".fleet",
        "Pods", "DerivedData", ".swiftpm", "CMakeFiles", ".terraform", ".docker", ".android", ".cache",
    };

    public static bool IsExcludedDirectory(string path) => ExcludedDirectoryNames.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));

    private static EnumerationOptions TopLevel(bool showHidden) => showHidden ? TopLevelWithHidden : TopLevelOnly;

    /// <summary>递归遍历跳过的属性；联接和符号链接始终跳过，避免回到祖先目录无限遍历。</summary>
    public static FileAttributes TraversalSkip(bool showHidden) =>
        FileAttributes.System | FileAttributes.ReparsePoint | (showHidden ? 0 : FileAttributes.Hidden);

    /// <summary>当前目录（不递归）下的图片和视频，按资源管理器的自然顺序排列。</summary>
    public static List<MediaFile> Scan(string folder, CancellationToken ct = default, bool showHidden = false)
    {
        ct.ThrowIfCancellationRequested();
        var items = new List<MediaFile>();
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", TopLevel(showHidden)))
        {
            ct.ThrowIfCancellationRequested();
            if (GetMediaKind(file.FullName) is { } kind)
                items.Add(new MediaFile(file.FullName, kind) { Version = FileVersion.FromInfo(file), CreatedTicks = GetCreatedTicks(file) });
        }
        items.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));
        ct.ThrowIfCancellationRequested();
        return items;
    }

    // 判断“没有媒体”必须遍历整棵子树，网络盘上的缓存/依赖目录可能要几秒；超过时限的目录按可能含媒体保留，
    // 避免一个大目录拖住整页。真正的封面查找在卡片上异步进行。
    public static readonly TimeSpan MediaProbeBudget = TimeSpan.FromSeconds(2);

    public static List<MediaFile> ScanEntries(string folder, CancellationToken ct = default,
        MediaFilter filter = MediaFilter.All, bool showSubfolders = true, bool showHidden = false)
    {
        var files = Scan(folder, ct, showHidden).Where(file => MatchesFilter(file.Kind, filter)).ToList();
        if (!showSubfolders) return files;
        // 无法访问的目录找不到媒体，会和空目录一样被过滤掉。
        var subfolders = GetSubfolders(folder, ct, showHidden).Where(path => !IsExcludedDirectory(path)).ToList();
        var hasMedia = ParallelTest(subfolders, (path, token) => ContainsMediaWithin(path, filter, showHidden, token), ct);
        var entries = subfolders.Where((_, i) => hasMedia[i])
            .Select(path => new MediaFile(path, MediaKind.Folder)
            {
                FolderModifiedTicks = GetFolderModifiedTicks(path),
                CreatedTicks = GetCreatedTicks(new DirectoryInfo(path)),
                PreviewFilter = filter,
                PreviewHidden = showHidden,
            }).ToList();
        entries.AddRange(files);
        ct.ThrowIfCancellationRequested();
        return entries;
    }

    public static List<MediaFile> FindFolderPreviews(string folder, CancellationToken ct = default, MediaFilter filter = MediaFilter.All,
        bool showHidden = false) => FindFolderPreviews(folder, 8, ct, filter, showHidden);

    public static bool ContainsMediaRecursive(string folder, CancellationToken ct = default, MediaFilter filter = MediaFilter.All,
        bool showHidden = false) => FindFolderPreviews(folder, 1, ct, filter, showHidden).Count > 0;

    /// <summary>限时判断子树是否含媒体；超时返回 true（宁可多显示一个入口），外部取消仍然抛出。</summary>
    public static bool ContainsMediaWithin(string folder, MediaFilter filter, bool showHidden, CancellationToken ct, TimeSpan? budget = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeout = budget ?? MediaProbeBudget;
        // CancelAfter(0) 由计时器异步触发，零时限需要立即取消。
        if (timeout <= TimeSpan.Zero) limit.Cancel();
        else limit.CancelAfter(timeout);
        try { return ContainsMediaRecursive(folder, limit.Token, filter, showHidden); }
        catch (OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return true;
        }
    }

    // 网络盘的耗时主要是逐个目录往返，并发检查兄弟目录可以把整页等待从各目录之和降到最慢的一个。
    public static bool[] ParallelTest(IReadOnlyList<string> paths, Func<string, CancellationToken, bool> test, CancellationToken ct)
        => ParallelMap(paths, test, ct);

    private static T[] ParallelMap<T>(IReadOnlyList<string> paths, Func<string, CancellationToken, T> map, CancellationToken ct)
    {
        var results = new T[paths.Count];
        Parallel.For(0, paths.Count, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, (i, state) =>
        {
            try { results[i] = map(paths[i], ct); }
            catch (OperationCanceledException) { state.Stop(); }
        });
        ct.ThrowIfCancellationRequested();
        return results;
    }

    private static List<MediaFile> FindFolderPreviews(string folder, int limit, CancellationToken ct, MediaFilter filter, bool showHidden)
        => EnumerateFolderPreviews(folder, ct, filter, showHidden).Take(limit).ToList();

    public static IEnumerable<MediaFile> EnumerateFolderPreviews(string folder, CancellationToken ct = default,
        MediaFilter filter = MediaFilter.All, bool showHidden = false)
    {
        ct.ThrowIfCancellationRequested();
        bool accessible;
        try
        {
            accessible = (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { accessible = false; }
        if (!accessible) yield break;
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.TryPop(out var current))
        {
            ct.ThrowIfCancellationRequested();
            // 枚举也是惰性的，拿到足够封面后不再访问后面的目录。
            foreach (var file in EnumeratePreviewFiles(current, showHidden, ct))
            {
                if (GetMediaKind(file.FullName) is { } kind && MatchesFilter(kind, filter))
                    yield return new(file.FullName, kind) { Version = FileVersion.FromInfo(file), CreatedTicks = GetCreatedTicks(file) };
            }
            try
            {
                var directories = new List<string>();
                var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = TraversalSkip(showHidden) };
                foreach (var directory in Directory.EnumerateDirectories(current, "*", options))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!IsExcludedDirectory(directory)) directories.Add(directory);
                }
                directories.Sort((a, b) => CompareNames(Path.GetFileName(a), Path.GetFileName(b)));
                for (int i = directories.Count - 1; i >= 0; i--) pending.Push(directories[i]);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        }
        ct.ThrowIfCancellationRequested();
    }

    private static IEnumerable<FileInfo> EnumeratePreviewFiles(string folder, bool showHidden, CancellationToken ct)
    {
        IEnumerator<FileInfo>? iterator = null;
        try { iterator = new DirectoryInfo(folder).EnumerateFiles("*", TopLevel(showHidden)).GetEnumerator(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        if (iterator is null) yield break;
        using (iterator)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                FileInfo? next;
                try { next = iterator.MoveNext() ? iterator.Current : null; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { next = null; }
                if (next is null) yield break;
                yield return next;
            }
        }
    }

    public static IEnumerable<string> SupportedExtensions => ImageExtensions.Concat(VideoExtensions);

    public static MediaKind? GetMediaKind(string path)
    {
        var extension = Path.GetExtension(path);
        if (ImageExtensions.Contains(extension)) return MediaKind.Image;
        if (VideoExtensions.Contains(extension)) return MediaKind.Video;
        return null;
    }

    public static bool MatchesFilter(MediaKind kind, MediaFilter filter) => filter switch
    {
        MediaFilter.Photos => kind == MediaKind.Image,
        MediaFilter.Videos => kind == MediaKind.Video,
        _ => kind is MediaKind.Image or MediaKind.Video,
    };

    private static long? GetCreatedTicks(FileSystemInfo info)
    {
        try { return info.Exists ? info.CreationTimeUtc.Ticks : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static bool HasMedia(string folder, CancellationToken ct = default, MediaFilter filter = MediaFilter.All, bool showHidden = false)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", TopLevel(showHidden)))
            {
                ct.ThrowIfCancellationRequested();
                if (GetMediaKind(path) is { } kind && MatchesFilter(kind, filter)) return true;
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
        if (Directory.Exists(pictures)) roots.Add(new(pictures, "图片") { ModifiedTicks = ReadFolderModifiedTicks(pictures) });
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (Directory.Exists(desktop)) roots.Add(new(desktop, "桌面") { ModifiedTicks = ReadFolderModifiedTicks(desktop) });
        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!drive.IsReady) continue;
                var label = drive.VolumeLabel;
                roots.Add(new(drive.RootDirectory.FullName, string.IsNullOrEmpty(label)
                    ? drive.Name : $"{label} ({drive.Name.TrimEnd('\\')})") { ModifiedTicks = ReadFolderModifiedTicks(drive.RootDirectory.FullName) });
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // 枚举期间可移动磁盘可能已拔出。
            }
        }
        return roots;
    }

    public static List<string> GetSubfolders(string folder, CancellationToken ct = default, bool showHidden = false)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var dirs = new List<string>();
            foreach (var path in Directory.EnumerateDirectories(folder, "*", TopLevel(showHidden)))
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

    public static List<FolderChild> GetChildFolders(string folder, CancellationToken ct = default, bool showHidden = false)
    {
        var children = ParallelMap(GetSubfolders(folder, ct, showHidden), (path, token) =>
        {
            if (showHidden && !IsVisibleHiddenEntry(path, token)) return null;
            // 能列出名称不代表能访问目录；失败节点不应留下一个无法打开的入口。
            return TryGetHasSubfolders(path, showHidden, token, out var hasChildren)
                ? new FolderChild(path, hasChildren) { ModifiedTicks = ReadFolderModifiedTicks(path) } : null;
        }, ct);
        return children.OfType<FolderChild>().ToList();
    }

    /// <summary>
    /// 显示隐藏项时，隐藏目录只有含图片或视频才作为导航入口，避免 AppData、.git 之类的目录淹没文件树。
    /// 普通目录不受影响，仍保留完整导航。
    /// </summary>
    public static bool IsVisibleHiddenEntry(string path, CancellationToken ct = default)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Hidden) == 0) return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return false; }
        if (IsExcludedDirectory(path)) return false;
        return ContainsMediaWithin(path, MediaFilter.All, showHidden: true, ct);
    }

    public static bool HasSubfolders(string folder, bool showHidden = false) =>
        TryGetHasSubfolders(folder, showHidden, default, out var result) && result;

    public static long? ReadFolderModifiedTicks(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            return directory.Exists ? directory.LastWriteTimeUtc.Ticks : null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException) { return null; }
    }

    private static bool TryGetHasSubfolders(string folder, bool showHidden, CancellationToken ct, out bool hasChildren)
    {
        try
        {
            // 先找普通子目录，只有没有时才递归检查隐藏子目录，展开前的判断尽量便宜。
            hasChildren = Directory.EnumerateDirectories(folder, "*", TopLevelOnly).Any()
                || showHidden && Directory.EnumerateDirectories(folder, "*", TopLevelWithHidden)
                    .Any(path => IsVisibleHiddenEntry(path, ct));
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
