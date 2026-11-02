using Fallery.Controls;
using Fallery.Services;
using Fallery.ViewModels;
using Microsoft.UI.Xaml;

namespace Fallery;

public sealed partial class MainWindow
{
    public GalleryViewModel GalleryState { get; } = new();
    public ViewerViewModel ViewerState { get; } = new();
    private CancellationTokenSource? _treeSelectionCts;

    public Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ToCollapsedVisibility(bool visible) => visible ? Visibility.Collapsed : Visibility.Visible;

    private void InitializeViewModels()
    {
        InitializeBrowsePositions();
        ViewerState.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewerState.IsOpen)) UpdateViewerBackdrop();
            if (args.PropertyName == nameof(ViewerState.CurrentItem)) UpdateFilmstripSelection();
        };
        GalleryState.ItemsChanging += () =>
        {
            EndMarqueeSelection(cancel: true);
            RememberBrowsePosition();
            ReleaseGalleryThumbnails();
            // 保留显式断开数据源，确保布局先回收旧卡片再创建新的绑定。
            LiveText.SetOnce(Gallery, "ItemsSource", localized => localized.ItemsSource = null);
        };
        GalleryState.ItemsChanged += () =>
        {
            bool live = IsLiveFolderRefresh;
            bool wasViewing = ViewerState.IsOpen;
            if (_selectedItem is not null && !GalleryState.Items.Contains(_selectedItem)) _selectedItem = null;
            if (live && _liveRefreshSelection is { } selectedPath)
                _selectedItem = GalleryState.Items.FirstOrDefault(item => string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
            if (live) RestoreItemSelection(_selectedPaths.ToArray(), _selectedItem?.Path, _selectionAnchor);
            LiveText.SetOnce(Gallery, "ItemsSource", localized => localized.ItemsSource = GalleryState.Items);
            bool adopted = AdoptLaunchPlaylist();
            if (!adopted) ViewerState.SetPlaylist(GalleryState.MediaItems);
            if (live)
            {
                CancelBrowsePositionRestore();
                if (wasViewing && !ViewerState.IsOpen) CloseViewer();
                int version = _navigationVersion;
                double vertical = _liveRefreshOffset, horizontal = _liveRefreshHorizontalOffset;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (_closed || version != _navigationVersion) return;
                    GalleryScroller.UpdateLayout();
                    GalleryScroller.ChangeView(horizontal, vertical, null, disableAnimation: true);
                });
            }
            else RestoreBrowsePosition(adopted ? _selectedItem?.Path : null);
            _displayedBrowseFolder = GalleryState.CurrentFolder;
            UpdateGalleryVisibility();
        };
        GalleryState.LayoutInvalidated += () => { Masonry.Refresh(); QueueBrowsePositionRestore(); };
        GalleryState.FolderCommitted += (folder, ct) =>
        {
            RefreshAddressBar();
            QueueSettingsSave();
            if (IsLiveFolderRefresh) return;
            FolderTree.SelectedNode = null;
            _currentFolderNode = null;
            _ = SynchronizeTreeAsync(folder, ct);
            // 打开方式启动时查看器已在播放该文件，横排模式不要再跳回第一项。
            if (IsFilmstrip && !ViewerState.IsOpen) _ = ShowInViewerAsync(0);
        };
        GalleryState.PropertyChanged += (_, args) =>
        {
            if (!_closed && args.PropertyName == nameof(GalleryState.IsScanning)) UpdateGalleryVisibility();
        };
    }

    private async Task SynchronizeTreeAsync(string folder, CancellationToken ct)
    {
        _treeSelectionCts?.Cancel();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
        _treeSelectionCts = cts;
        try { await SelectCurrentFolderAsync(folder, cts.Token); }
        finally { if (ReferenceEquals(_treeSelectionCts, cts)) _treeSelectionCts = null; }
    }

    private async void ClearCache_Click(object sender, RoutedEventArgs e) => await ClearCacheAsync();

    private async Task ClearCacheAsync()
    {
        if (_closed || _closing) return;
        GalleryState.Cancel();
        ReleaseGalleryThumbnails();
        LiveText.SetOnce(Gallery, "ItemsSource", localized => localized.ItemsSource = null);
        GalleryState.ClearCachedDirectories();
        MediaCache.Clear();
        try
        {
            await MediaCache.Disk.ClearAsync();
            GalleryState.Status = AppText.Get("MemoryAndDiskCachesClearedPreviewsWillReloadAs");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GalleryState.Status = AppText.Get("MemoryCacheClearedSomeDiskCacheFilesCouldNot");
        }
        finally { if (!_closed && !_closing) LiveText.SetOnce(Gallery, "ItemsSource", localized => localized.ItemsSource = GalleryState.Items); }
    }
}
