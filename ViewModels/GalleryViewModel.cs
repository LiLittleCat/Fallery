using CommunityToolkit.Mvvm.ComponentModel;
using Fallery.Models;
using Fallery.Services;

namespace Fallery.ViewModels;

public sealed partial class GalleryViewModel : ObservableObject, IDisposable
{
    private readonly Stack<string> _history = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly List<string> _recentFolders = [];
    public const int RecentFolderLimit = 20;
    // 只保留有限目录的模型；图片引用在离开目录时释放，由独立像素预算管理。
    private readonly BoundedCache<string, List<MediaItem>> _directories = new(20_000, 32);
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _sortCts;
    private int _sortVersion;
    private bool _disposed;
    public event Action? ItemsChanging;
    public event Action? ItemsChanged;
    public event Action? LayoutInvalidated;
    public event Action<string, CancellationToken>? FolderCommitted;
    public List<MediaItem> Items { get; private set; } = [];
    public List<MediaItem> MediaItems { get; private set; } = [];
    public IReadOnlyList<FolderEntry> Breadcrumbs { get; private set; } = [];
    public string? PendingFolder { get; private set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack), nameof(CanGoForward), nameof(CanGoUp))]
    public partial bool IsScanning { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoUp))]
    public partial string? CurrentFolder { get; set; }
    [ObservableProperty]
    public partial string Status { get; set; } = "在左侧选择一个文件夹";
    [ObservableProperty]
    public partial MediaSortField SortField { get; set; }
    [ObservableProperty]
    public partial bool Descending { get; set; }
    [ObservableProperty]
    public partial GalleryDisplayMode DisplayMode { get; set; }
    [ObservableProperty]
    public partial bool ShowFileNames { get; set; } = true;
    [ObservableProperty]
    public partial bool ShowCards { get; set; } = true;
    [ObservableProperty]
    public partial MediaFilter Filter { get; set; }
    [ObservableProperty]
    public partial bool ShowSubfolders { get; set; } = true;
    [ObservableProperty]
    public partial bool ShowHidden { get; set; }
    public bool CanGoBack => !IsScanning && _history.Count > 0;
    public string? PreviousFolder => _history.TryPeek(out var folder) ? folder : null;
    public bool CanGoForward => !IsScanning && _forwardHistory.Count > 0;
    public string? NextFolder => _forwardHistory.TryPeek(out var folder) ? folder : null;
    public string? ParentFolder => CurrentFolder is null ? null : Path.GetDirectoryName(CurrentFolder);
    public bool CanGoUp => !IsScanning && ParentFolder is not null;
    public bool IsSearching => _searchCts is not null;
    /// <summary>最近成功打开的目录，新的在前；与后退/前进栈分开，跨进程保留。</summary>
    public IReadOnlyList<string> RecentFolders => _recentFolders;

    public void RestoreRecentFolders(IEnumerable<string> folders)
    {
        _recentFolders.Clear();
        foreach (var folder in folders.Reverse()) AddRecentFolder(folder);
    }

    public void ClearRecentFolders() => _recentFolders.Clear();

    private void AddRecentFolder(string folder)
    {
        _recentFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        _recentFolders.Insert(0, folder);
        if (_recentFolders.Count > RecentFolderLimit) _recentFolders.RemoveRange(RecentFolderLimit, _recentFolders.Count - RecentFolderLimit);
    }

    public async Task<bool> NavigateAsync(string folder, bool pushHistory = true, bool refresh = false, bool forward = false)
    {
        if (_disposed) return false;
        CancelSearch();
        _sortCts?.Cancel();
        _sortVersion++;
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!refresh && !IsScanning && string.Equals(folder, CurrentFolder, StringComparison.OrdinalIgnoreCase)) return false;
        _loadCts?.Cancel();
        using var cts = new CancellationTokenSource();
        _loadCts = cts;
        PendingFolder = folder;
        IsScanning = true;
        Status = $"正在扫描：{folder}";
        try
        {
            var filter = Filter;
            bool showSubfolders = ShowSubfolders;
            bool showHidden = ShowHidden;
            var files = await Task.Run(() => FolderScanner.ScanEntries(folder, cts.Token, filter, showSubfolders, showHidden), cts.Token);
            while (true)
            {
                var field = SortField;
                bool descending = Descending;
                if (field == MediaSortField.Taken)
                {
                    Status = "正在读取拍摄日期…";
                    files = await MediaDates.ReadAsync(files, cts.Token);
                }
                files = await Task.Run(() => MediaSorter.Sort(files, f => f, field, descending), cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                if (field == SortField && descending == Descending) break;
            }
            // 扫描成功才移动历史游标；失败或取消不能吞掉后退/前进记录。
            if (forward && string.Equals(NextFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                if (CurrentFolder is not null) _history.Push(CurrentFolder);
                _forwardHistory.Pop();
            }
            else if (!pushHistory && string.Equals(PreviousFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                if (CurrentFolder is not null) _forwardHistory.Push(CurrentFolder);
                _history.Pop();
            }
            else if (pushHistory && CurrentFolder is not null && !string.Equals(folder, CurrentFolder, StringComparison.OrdinalIgnoreCase))
            {
                _history.Push(CurrentFolder);
                _forwardHistory.Clear();
            }
            CurrentFolder = folder;
            AddRecentFolder(folder);
            var segments = new List<FolderEntry>();
            for (var directory = new DirectoryInfo(folder); directory is not null; directory = directory.Parent)
                segments.Insert(0, new(directory.FullName, directory.Parent is null ? directory.FullName.TrimEnd('\\') : directory.Name));
            Breadcrumbs = segments;
            OnPropertyChanged(nameof(Breadcrumbs));
            _directories.TryGet(folder.ToUpperInvariant(), out var previous);
            var reusable = (previous ?? []).Where(i => !i.IsFolder && i.Version is not null)
                .ToDictionary(i => i.Path, StringComparer.Ordinal);
            var next = files.Select(f => f.Version is not null
                && reusable.TryGetValue(f.Path, out var old) && old.Version == f.Version
                && old.Kind == f.Kind && old.File.CreatedTicks == f.CreatedTicks ? old : new MediaItem(f)).ToList();
            ReplaceItems(next);
            _directories.Set(folder.ToUpperInvariant(), next, Math.Max(1, next.Count));
            IsScanning = false;
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            UpdateStatus();
            FolderCommitted?.Invoke(folder, cts.Token);
            await ProbeAspectRatiosAsync(MediaItems, cts.Token);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!cts.IsCancellationRequested && !_disposed) Status = $"无法打开这个文件夹：{ex.Message}";
            return false;
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts)) { _loadCts = null; PendingFolder = null; IsScanning = false; }
        }
    }

    private async Task ProbeAspectRatiosAsync(List<MediaItem> items, CancellationToken ct)
    {
        foreach (var batch in items.Where(i => !i.HasAspectRatio).Chunk(200))
        {
            var results = await Task.WhenAll(batch.Select(item => Task.Run(async () =>
            {
                // 限制元数据探测并发，避免大目录同时打开成千上万个文件。
                await ProbeSlots.WaitAsync(ct);
                try
                {
                    double? ratio = await MediaProbe.TryGetAspectRatioAsync(item.Path, item.Kind, ct);
                    bool unchanged = item.Version is { } key && FileVersion.Read(item.Path) == key;
                    return (Item: item, Ratio: ratio, Unchanged: unchanged);
                }
                finally { ProbeSlots.Release(); }
            }, ct)));
            ct.ThrowIfCancellationRequested();
            foreach (var result in results)
            {
                if (result.Ratio is not { } ratio || !result.Unchanged) continue;
                result.Item.AspectRatio = ratio;
                result.Item.HasAspectRatio = true;
                if (result.Unchanged && result.Item.Version is { } key)
                {
                    MediaCache.AspectRatios.Set(key, ratio);
                }
            }
            LayoutInvalidated?.Invoke();
        }
    }
    private static readonly SemaphoreSlim ProbeSlots = new(4);

    private void ReplaceItems(List<MediaItem> items)
    {
        ItemsChanging?.Invoke();
        foreach (var item in Items) item.ReleaseThumbnail();
        Items = items;
        MediaItems = items.Where(i => !i.IsFolder).ToList();
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(MediaItems));
        ItemsChanged?.Invoke();
    }

    /// <summary>删除后只从当前列表移除该项，不整页重扫，也不打乱排序。</summary>
    public bool RemoveItem(string path)
    {
        int index = Items.FindIndex(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        var next = new List<MediaItem>(Items);
        next.RemoveAt(index);
        ReplaceItems(next);
        if (CurrentFolder is { } folder) _directories.Set(folder.ToUpperInvariant(), next, Math.Max(1, next.Count));
        UpdateStatus();
        return true;
    }

    public async Task ApplySortAsync()
    {
        int version = ++_sortVersion;
        _sortCts?.Cancel();
        if (IsScanning || _disposed) return;
        using var cts = new CancellationTokenSource();
        _sortCts = cts;
        var original = Items;
        var field = SortField;
        bool descending = Descending;
        try
        {
            var metadata = original.Select(item => item.File).ToList();
            if (field == MediaSortField.Taken)
            {
                Status = "正在读取拍摄日期…";
                metadata = await MediaDates.ReadAsync(metadata, cts.Token);
            }
            var byPath = metadata.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            var sorted = await Task.Run(() => MediaSorter.Sort(original, item => byPath[item.Path], field, descending), cts.Token);
            if (!_disposed && version == _sortVersion && ReferenceEquals(original, Items))
            {
                ReplaceItems(sorted);
                UpdateStatus();
            }
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_sortCts, cts)) _sortCts = null; }
    }

    private void UpdateStatus()
    {
        int videos = MediaItems.Count(item => item.IsVideo);
        string filter = Filter switch { MediaFilter.Photos => "仅图片 · ", MediaFilter.Videos => "仅视频 · ", _ => "" };
        Status = $"{filter}{Items.Count - MediaItems.Count} 个文件夹 · {MediaItems.Count - videos} 张图片 · {videos} 个视频";
    }

    public async Task<string?> FindAdjacentAsync(bool forward)
    {
        if (CurrentFolder is null || IsScanning || _disposed) return null;
        CancelSearch();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _searchCts = cts;
        string current = CurrentFolder;
        Status = "正在查找含图片或视频的目录…（Esc 取消）";
        try
        {
            var filter = Filter;
            bool showHidden = ShowHidden;
            var target = await Task.Run(() => FolderNavigator.FindAdjacent(current, forward, cts.Token, filter: filter, showHidden: showHidden), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (_disposed) return null;
            if (target is null) Status = forward ? "已经是最后一个媒体目录" : "已经是第一个媒体目录";
            return target;
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && ReferenceEquals(_searchCts, cts)) Status = "目录查找已取消或超时";
            return null;
        }
        catch (IOException ex)
        {
            if (!_disposed && ReferenceEquals(_searchCts, cts)) Status = ex.Message;
            return null;
        }
        finally { if (ReferenceEquals(_searchCts, cts)) _searchCts = null; }
    }

    public void CancelSearch() { _searchCts?.Cancel(); _searchCts = null; }
    public void Cancel() { _loadCts?.Cancel(); _sortCts?.Cancel(); CancelSearch(); _sortVersion++; }
    public void ClearCachedDirectories() => _directories.Clear();
    public void Dispose()
    {
        _disposed = true;
        Cancel();
        foreach (var item in Items) item.ReleaseThumbnail();
        _directories.Clear();
    }
}
