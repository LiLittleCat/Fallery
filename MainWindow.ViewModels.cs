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
    public ElementTheme ViewerChromeTheme(bool open, Fallery.Models.GalleryDisplayMode mode) =>
        open && mode != Fallery.Models.GalleryDisplayMode.Filmstrip ? ElementTheme.Dark : ElementTheme.Default;

    private void InitializeViewModels()
    {
        ViewerState.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewerState.CurrentItem)) UpdateFilmstripSelection();
        };
        GalleryState.ItemsChanging += () =>
        {
            ReleaseGalleryThumbnails();
            // 保留显式断开数据源，确保布局先回收旧卡片再创建新的绑定。
            Gallery.ItemsSource = null;
        };
        GalleryState.ItemsChanged += () =>
        {
            Gallery.ItemsSource = GalleryState.Items;
            ViewerState.SetPlaylist(GalleryState.MediaItems);
            GalleryScroller.ChangeView(null, 0, null, disableAnimation: true);
            UpdateGalleryVisibility();
        };
        GalleryState.LayoutInvalidated += () => Masonry.Refresh();
        GalleryState.FolderCommitted += (folder, ct) =>
        {
            RefreshAddressBar();
            QueueSettingsSave();
            FolderTree.SelectedNode = null;
            _currentFolderNode = null;
            _ = SynchronizeTreeAsync(folder, ct);
            if (IsFilmstrip) _ = ShowInViewerAsync(0);
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

    private void ClearCache_Click(object sender, RoutedEventArgs e) => ClearCacheAsync();

    private Task ClearCacheAsync()
    {
        if (_closed || _closing) return Task.CompletedTask;
        GalleryState.Cancel();
        ReleaseGalleryThumbnails();
        Gallery.ItemsSource = null;
        GalleryState.ClearCachedDirectories();
        MediaCache.Clear();
        Gallery.ItemsSource = GalleryState.Items;
        GalleryState.Status = "内存缓存已清除，预览会按需重新加载";
        return Task.CompletedTask;
    }
}
