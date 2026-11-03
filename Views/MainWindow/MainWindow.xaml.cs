using Fallery.Layouts;
using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Fallery.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace Fallery;

public sealed partial class MainWindow : Window
{

    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Dictionary<UIElement, MediaItem> _realizedItems = [];
    private readonly Dictionary<TreeViewNode, Task> _expandingNodes = [];
    private Task? _rootsTask;
    private TreeViewNode? _currentFolderNode;
    private Services.Playback.IPlayer? _videoPlayer;
    private AppWindowPresenter? _presenterBeforeFullScreen;
    private bool _initialized;
    private int _themeIndex;
    private MediaItem? _selectedItem;
    private static readonly SemaphoreSlim ViewerImageDecodeSlots = new(2);
    private bool _closed;
    private bool _pickingFolder;
    private static int _openWindowCount;
    private Windows.Foundation.Point? _panOrigin;
    private double _panHorizontalOffset;
    private double _panVerticalOffset;

    public MainWindow(AppSettingsStore? settingsStore = null, string? startupPath = null, bool monitorFolderChanges = true)
    {
        if (settingsStore is not null) _settingsStore = settingsStore;
        _startupPath = startupPath;
        InitializeComponent();
        UpdateIndicator.Attach(SettingsButton, SettingsUpdateDot, "Settings", VelopackUpdates.Shared);
        Title = AppTitleBar.Title = AppIdentity.DisplayName;
        _openWindowCount++;
        // Win10 不支持 Mica，换成系统亚克力，避免透明外框露出黑底。
        if (!Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            SystemBackdrop = new DesktopAcrylicBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fallery.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppTitleBar.LayoutUpdated += TitleBar_LayoutUpdated;
        Root.ActualThemeChanged += Root_ActualThemeChanged;
        AppTitleBar.ActualThemeChanged += Root_ActualThemeChanged;
        Root.Loaded += Root_Loaded;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
        InitializeExperience();
        InitializeViewModels();
        InitializeFavorites();
        InitializeStickyTree();
        InitializePlayback();
        InitializeHoverPreview();
        InitializeFullScreenControls();
        InitializeViewerInfo();
        InitializeViewerSidePanel();
        RefreshAddressBar();
        if (monitorFolderChanges) InitializeFolderMonitoring();
        Root.Language = AppText.Language;
        AppText.Changed += OnLanguageChanged;
    }

    // 0 跟随系统、1 亮色、2 暗色，与 ElementTheme 取值一致。系统标题栏不属于 XAML 树，需要和页面主题分别同步。
    private void ApplyTheme(int index)
    {
        _themeIndex = Math.Clamp(index, 0, 2);
        Root.RequestedTheme = (ElementTheme)_themeIndex;
        UpdateTitleBarTheme();
        QueueSettingsSave();
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateTitleBarTheme();
        if (_initialized) return;
        _initialized = true;
        await RestoreSettingsAsync();
        if (_closed) return;
        if (_startupPath is { } startup)
        {
            // 打开方式启动：不等磁盘列表和上次目录，直接打开文件。
            _rootsTask ??= LoadRootsAsync();
            await OpenPathAsync(startup);
            return;
        }
        await (_rootsTask ??= LoadRootsAsync());
        if (!_closed && _navigationVersion == 0 && _lastFolderToRestore is { } folder)
            await NavigateAsync(folder, pushHistory: false);
    }

    private async Task LoadRootsAsync()
    {
        try
        {
            var roots = await Task.Run(() => FolderScanner.GetRoots(_lifetimeCts.Token));
            if (_closed) return;
            int rootIndex = 0;
            foreach (var entry in roots)
            {
                if (IsTreeFolderExcluded(entry.Path)) continue;
                if (!FolderTree.RootNodes.Any(node =>
                    node.Content is FolderEntry existing && string.Equals(existing.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))
                    // 恢复被排除的快捷入口时也插回顶部，不追加到磁盘之后。
                    FolderTree.RootNodes.Insert(Math.Min(rootIndex, FolderTree.RootNodes.Count), CreateNode(entry.Path, entry.Name, entry.ModifiedTicks, entry.CreatedTicks));
                rootIndex++;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed) GalleryState.Status = AppText.Format("CouldNotReadDrives", ex.Message);
        }
    }
    private void Root_ActualThemeChanged(FrameworkElement sender, object args) => UpdateTitleBarTheme();
    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args) => UpdateTitleBarTheme();

    private void UpdateTitleBarTheme()
    {
        // 关闭时 XAML 仍可能发出主题通知，此时原生窗口已不可用。
        if (_closed || _closing || Root is null || AppWindow is not { } appWindow) return;
        bool dark = AppTitleBar.ActualTheme == ElementTheme.Dark;
        var titleBar = appWindow.TitleBar;
        // 系统窗口按钮不继承 RequestedTheme，使用 SDK 的标题栏主题接口同步。
        titleBar.PreferredTheme = dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        var background = dark ? Microsoft.UI.ColorHelper.FromArgb(255, 48, 50, 52)
            : Microsoft.UI.ColorHelper.FromArgb(255, 236, 237, 239);
        var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var muted = dark ? Microsoft.UI.ColorHelper.FromArgb(255, 160, 160, 160)
            : Microsoft.UI.ColorHelper.FromArgb(255, 96, 96, 96);
        var hover = dark ? Microsoft.UI.ColorHelper.FromArgb(255, 48, 48, 48)
            : Microsoft.UI.ColorHelper.FromArgb(255, 229, 229, 229);
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = muted;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveForegroundColor = muted;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = hover;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    // ───────────── 文件树 ─────────────

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        AppText.Changed -= OnLanguageChanged;
        EndMarqueeSelection(cancel: true);
        _closed = true;
        DetachStickyTree();
        StopHoverPreview();
        _fullScreenControlsTimer.Stop();
        _folderChangeTimer?.Stop();
        _folderMonitor?.Dispose();
        AppTitleBar.LayoutUpdated -= TitleBar_LayoutUpdated;
        _settingsDialog?.Hide();
        _searchDialog?.Hide();
        CloseItemMenu();
        CloseAddressMenu();
        GalleryState.Dispose();
        ViewerState.Close();
        _lifetimeCts.Cancel();
        GalleryState.CancelSearch();
        _settingsTimer.Stop();
        CancelGesture();
        CloseViewer();
        _taskbarProgress?.Dispose();
        ReleaseGalleryThumbnails();
        if (--_openWindowCount == 0) MediaCache.Clear();
        if (_searchIndex is not null && _searchProgressHandler is not null) _searchIndex.Progress -= _searchProgressHandler;
    }

    private TreeViewNode CreateNode(string path, string name, long? modifiedTicks = null, long? createdTicks = null) => new()
    {
        Content = new FolderEntry(path, name) { ModifiedTicks = modifiedTicks, CreatedTicks = createdTicks,
            ShowModifiedTime = _treeShowModifiedTime, TimeField = _treeTimeField },
        HasUnrealizedChildren = true,
    };

    private async void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        try { await EnsureChildrenLoadedAsync(args.Node); }
        catch (OperationCanceledException) { }
    }

    private async Task EnsureChildrenLoadedAsync(TreeViewNode node)
    {
        if (_expandingNodes.TryGetValue(node, out var pending))
        {
            await pending;
            return;
        }
        if (!node.HasUnrealizedChildren || node.Content is not FolderEntry entry) return;
        var loading = LoadChildrenAsync(node, entry);
        _expandingNodes.Add(node, loading);
        try { await loading; }
        finally { _expandingNodes.Remove(node); }
    }

    private async Task LoadChildrenAsync(TreeViewNode node, FolderEntry entry)
    {
        bool showHidden = GalleryState.ShowHidden;
        var result = await Task.Run(() => (Children: FolderScanner.GetChildFolders(entry.Path, _lifetimeCts.Token, showHidden),
            ModifiedTicks: FolderScanner.ReadFolderModifiedTicks(entry.Path), CreatedTicks: FolderScanner.ReadFolderCreatedTicks(entry.Path)));
        _lifetimeCts.Token.ThrowIfCancellationRequested();
        LiveText.SetOnce(node, "Content", localized => localized.Content = entry with { ModifiedTicks = result.ModifiedTicks, CreatedTicks = result.CreatedTicks,
            ShowModifiedTime = _treeShowModifiedTime, TimeField = _treeTimeField });
        foreach (var childInfo in result.Children)
        {
            if (IsTreeFolderExcluded(childInfo.Path)) continue;
            var child = CreateNode(childInfo.Path, System.IO.Path.GetFileName(childInfo.Path), childInfo.ModifiedTicks, childInfo.CreatedTicks);
            child.HasUnrealizedChildren = childInfo.HasChildren;
            node.Children.Add(child);
        }
        node.HasUnrealizedChildren = false;
        SortTreeChildren(node);
        if (node.Parent is { } parent) SortTreeChildren(parent);
    }

    private static string NormalizeFolderPath(string path) =>
        System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));

    private static bool IsSameOrChildFolder(string folder, string ancestor)
    {
        folder = NormalizeFolderPath(folder);
        ancestor = NormalizeFolderPath(ancestor);
        return string.Equals(folder, ancestor, StringComparison.OrdinalIgnoreCase)
            || folder.StartsWith(ancestor.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase);
    }

    private async Task SelectCurrentFolderAsync(string folder, CancellationToken ct)
    {
        try
        {
            await (_rootsTask ??= LoadRootsAsync()).WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            var target = NormalizeFolderPath(folder);
            if (IsTreeFolderExcluded(target)) { ClearCurrentTreeSelection(); return; }
            // “图片”“桌面”与盘符可能指向同一目录树，优先使用最接近目标的根节点。
            var node = FolderTree.RootNodes
                .Where(n => n.Content is FolderEntry entry && IsSameOrChildFolder(target, entry.Path))
                .OrderByDescending(n => ((FolderEntry)n.Content).Path.Length)
                .FirstOrDefault();
            if (node is null)
            {
                var root = System.IO.Path.GetPathRoot(target)!;
                node = CreateNode(root, root.TrimEnd('\\'));
                FolderTree.RootNodes.Add(node);
            }

            var ancestor = NormalizeFolderPath(((FolderEntry)node.Content).Path);
            var relative = System.IO.Path.GetRelativePath(ancestor, target);
            if (relative != ".")
            {
                foreach (var segment in relative.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
                {
                    // 导航取消只停止定位；手动展开的同一节点仍可共享已启动的加载。
                    await EnsureChildrenLoadedAsync(node).WaitAsync(ct);
                    ct.ThrowIfCancellationRequested();
                    if (IsTreeFolderExcluded(target)) { ClearCurrentTreeSelection(); return; }
                    ancestor = System.IO.Path.Combine(ancestor, segment);
                    var child = node.Children.FirstOrDefault(n => n.Content is FolderEntry entry
                        && string.Equals(NormalizeFolderPath(entry.Path), ancestor, StringComparison.OrdinalIgnoreCase));
                    if (child is null)
                    {
                        // 显式打开的隐藏目录或扫描后新建目录也需要可定位。
                        child = CreateNode(ancestor, segment);
                        node.Children.Add(child);
                        SortTreeChildren(node);
                    }
                    node.IsExpanded = true;
                    node = child;
                }
            }
            ct.ThrowIfCancellationRequested();
            await EnsureChildrenLoadedAsync(node).WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (IsTreeFolderExcluded(target)) { ClearCurrentTreeSelection(); return; }
            node.IsExpanded = node.Children.Count > 0;
            _currentFolderNode = node;
            FolderTree.SelectedNode = node;
            FolderTree.UpdateLayout();
            // 尚未实现的虚拟节点没有容器，交给内部列表滚动后再实现，避免抢走键盘焦点。
            if (FindTreeList(FolderTree) is { } list) list.ScrollIntoView(node);
            else if (FolderTree.ContainerFromNode(node) is FrameworkElement container) container.StartBringIntoView();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            if (!_closed && !ct.IsCancellationRequested)
                GalleryState.Status += AppText.Format("CouldNotLocateFolderInTree", ex.Message);
        }
    }

    private static ListView? FindTreeList(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ListView list) return list;
            if (FindTreeList(child) is { } nested) return nested;
        }
        return null;
    }

    private async void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node) await NavigateTreeNodeAsync(node);
    }

    private async Task NavigateTreeNodeAsync(TreeViewNode node)
    {
        if (_closed || _closing || node.Content is not FolderEntry entry) return;
        var navigation = NavigateAsync(entry.Path);
        int version = _navigationVersion;
        await navigation;
        try
        {
            // 重复点击当前目录不会重新扫描，但仍应展开被用户收起的节点。
            if (_closed || _closing || version != _navigationVersion
                || !string.Equals(GalleryState.CurrentFolder, NormalizeFolderPath(entry.Path), StringComparison.OrdinalIgnoreCase)) return;
            await EnsureChildrenLoadedAsync(node);
            if (!_closed && !_closing && version == _navigationVersion)
                node.IsExpanded = node.Children.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            if (!_closed && version == _navigationVersion) GalleryState.Status += AppText.Format("CouldNotExpandFolder", ex.Message);
        }
    }

    // ───────────── 导航 ─────────────

    private async void OpenFolder_Click(object sender, RoutedEventArgs e) => await PickFolderAsync();

    private async Task PickFolderAsync()
    {
        if (_pickingFolder || _closing || _closed) return;
        _pickingFolder = true;
        string? path = null;
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id);
            var result = await picker.PickSingleFolderAsync();
            path = result?.Path;
        }
        catch (Exception ex)
        {
            if (!_closed) GalleryState.Status = AppText.Format("CouldNotOpenFolderPicker", ex.Message);
        }
        finally { _pickingFolder = false; }
        if (!_closed && path is not null) await NavigateAsync(path);
    }

    private Task NavigateAsync(string folder, bool pushHistory = true, bool refresh = false) =>
        NavigateCoreAsync(folder, pushHistory, refresh, forward: false);

    private async Task NavigateCoreAsync(string folder, bool pushHistory, bool refresh, bool forward)
    {
        if (_closed || _closing) return;
        EndMarqueeSelection(cancel: true);
        _navigationVersion++;
        PrepareBrowseNavigation(folder);
        int version = _navigationVersion;
        int previewIndex = ViewerState.CurrentIndex;
        CloseItemMenu();
        if (_addressRequest != _navigationVersion && AddressEditor.Visibility == Visibility.Visible)
            EndAddressEdit(cancelRequest: false);
        CloseAddressMenu();
        CancelGesture();
        if (refresh || GalleryState.IsScanning || !string.Equals(NormalizeFolderPath(folder), GalleryState.CurrentFolder, StringComparison.OrdinalIgnoreCase))
            CloseViewer();
        _pendingBrowseNavigation = (folder, pushHistory, forward);
        try
        {
            if (!await GalleryState.NavigateAsync(folder, pushHistory, refresh, forward)
                && !_closed && !_closing && version == _navigationVersion && !GalleryState.IsScanning)
            {
                FolderTree.SelectedNode = _currentFolderNode;
                RestoreBrowsePosition(_selectedItem?.Path);
                if (IsFilmstrip && !ViewerState.IsOpen && previewIndex >= 0) await ShowInViewerAsync(previewIndex);
            }
        }
        finally { if (version == _navigationVersion) _pendingBrowseNavigation = null; }
    }

    private async void Back_Click(object sender, RoutedEventArgs e) => await GoBackAsync();
    private async void Forward_Click(object sender, RoutedEventArgs e) => await GoForwardAsync();
    private async void Up_Click(object sender, RoutedEventArgs e) => await GoUpAsync();

    private async Task GoBackAsync()
    {
        if (GalleryState.CanGoBack && GalleryState.PreviousFolder is { } folder) await NavigateAsync(folder, pushHistory: false);
    }

    private async Task GoForwardAsync()
    {
        if (GalleryState.CanGoForward && GalleryState.NextFolder is { } folder)
            await NavigateCoreAsync(folder, pushHistory: false, refresh: false, forward: true);
    }

    private async Task GoUpAsync()
    {
        if (GalleryState.CanGoUp && GalleryState.ParentFolder is { } folder) await NavigateAsync(folder);
    }

    // ───────────── 瀑布流 ─────────────

    private void UpdateGalleryVisibility()
    {
        // 扫描期间不展示旧目录卡片，避免把仍在等待的数据误认为新目录内容。
        Gallery.Visibility = GalleryState.IsScanning ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = GalleryState.IsScanning || (IsFilmstrip ? GalleryState.MediaItems.Count : GalleryState.Items.Count) == 0 ? Visibility.Visible : Visibility.Collapsed;
        LiveText.Set(EmptyText, "Text", localized => localized.Text = GalleryState.IsScanning ? AppText.Get("ReadingFolder") : GalleryState.CurrentFolder is null
            ? AppText.Get("SelectAFolderOnTheLeftOrClickOpen") : GalleryState.Filter != MediaFilter.All || !GalleryState.ShowSubfolders
            ? AppText.Get("NoMediaMatchesTheCurrentFilterChangeTheFilter")
            : IsFilmstrip ? AppText.Get("NoPhotosOrVideosHereOpenASubfolderBelow") : AppText.Get("NoFoldersPhotosOrVideosToDisplay"));
    }

    private uint ThumbnailDecodeSize
    {
        get
        {
            double scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
            double logicalSize = IsDetailsView ? 64 : IsFilmstrip ? FilmstripTileWidth : ThumbSize.Value;
            return (uint)Math.Clamp(logicalSize * scale, 128, MediaItem.MaxThumbnailPixels);
        }
    }

    private void Gallery_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Index < 0 || args.Index >= GalleryState.Items.Count) return;
        var item = GalleryState.Items[args.Index];
        if (args.Element is MediaTile tile)
        {
            tile.PreviewHoverChanged -= GalleryTile_PreviewHoverChanged;
            tile.PreviewHoverChanged += GalleryTile_PreviewHoverChanged;
            tile.Bind(item);
            ConfigureTile(tile);
        }
        _realizedItems[args.Element] = item;
        _ = item.EnsureThumbnailAsync(ThumbnailDecodeSize);
    }

    private void Gallery_ElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is MediaTile tile) tile.Bind(null);
        if (_realizedItems.Remove(args.Element, out var item)) item.ReleaseThumbnail(preserveFolderPreview: true);
    }

    private async void Tile_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_marqueeStarted || _suppressMarqueeTap) { e.Handled = true; return; }
        if (sender is UIElement element && _realizedItems.TryGetValue(element, out var item))
        {
            e.Handled = true;
            if (IsControlDown() || IsShiftDown()) return;
            SelectItem(item);
            if (item.IsFolder) await NavigateAsync(item.Path);
            else await ShowInViewerAsync(GalleryState.MediaItems.IndexOf(item));
        }
    }

    private void ReleaseGalleryThumbnails()
    {
        foreach (var (element, item) in _realizedItems)
        {
            if (element is MediaTile tile) tile.Bind(null);
            item.ReleaseThumbnail();
        }
        _realizedItems.Clear();
    }

    private void ThumbSize_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_initialized) return;
        foreach (var item in _realizedItems.Values) _ = item.EnsureThumbnailAsync(ThumbnailDecodeSize);
        QueueSettingsSave();
    }

    // ───────────── 查看器 ─────────────

    private async Task ShowInViewerAsync(int index, bool allowWhileScanning = false)
    {
        if ((GalleryState.IsScanning && !allowWhileScanning) || index < 0 || index >= ViewerState.Playlist.Count || _closed || _closing) return;
        EndMarqueeSelection(cancel: true);
        StopHoverPreview();
        if (!ViewerState.IsOpen)
        {
            ResetViewerSidePanel();
            SelectItem(ViewerState.Playlist[index]);
            RememberBrowsePosition();
            CancelBrowsePositionRestore();
        }
        CaptureBrowseWindowPlacement();
        bool imageToImage = ViewerState.CurrentItem?.IsImage == true && ViewerState.Playlist[index].IsImage;
        var request = ViewerState.BeginOpen(index);
        if (request is null) return;
        var ct = request.Token;
        var item = request.Item;

        _panOrigin = null;
        ViewerImage.ReleasePointerCaptures();
        StopVideo(keepRotationButtonEnabled: imageToImage);
        ClearViewerImage(keepImageButtonsEnabled: imageToImage);
        ImageScroller.Visibility = item.IsPlaybackMedia ? Visibility.Collapsed : Visibility.Visible;
        UpdateViewerOverlays();
        _ = LoadViewerDetailsAsync(item);
        if (!IsFilmstrip) CloseViewerButton.Focus(FocusState.Programmatic);

        try
        {
            if (item.IsPlaybackMedia)
            {
                var player = CreatePlayer(item, ct);
                _videoPlayer = player;
                ViewerVideo.Opacity = 0;
                ViewerVideo.IsHitTestVisible = false;
                ViewerVideo.Visibility = Visibility.Visible;
                if (item.IsAudio)
                {
                    AudioPresentation.Visibility = Visibility.Visible;
                    AudioPlaceholder.Visibility = Visibility.Visible;
                    LiveText.SetOnce(AudioTitle, "Text", localized => localized.Text = item.Name);
                    LiveText.SetOnce(AudioSubtitle, "Text", localized => localized.Text = "");
                    _ = LoadAudioArtworkAsync(item, ct);
                }
                await player.OpenAsync(item.Path, ct);
            }
            else
            {
                var file = await StorageFile.GetFileFromPathAsync(item.Path).AsTask(ct);
                ct.ThrowIfCancellationRequested();
                double ratio = await MediaProbe.TryGetAspectRatioAsync(item.Path, item.Kind, ct) ?? 1;
                ct.ThrowIfCancellationRequested();
                // 首次打开时覆盖层可能尚未布局，先确定视口，避免用零尺寸生成低清图片。
                ImageScroller.UpdateLayout();
                double scale = Root.XamlRoot?.RasterizationScale ?? 1;
                double viewportWidth = Math.Max(1, ImageScroller.ViewportWidth);
                double viewportHeight = Math.Max(1, ImageScroller.ViewportHeight);
                var bitmap = new BitmapImage { AutoPlay = true };
                // 只限制一个维度以保持比例；避免长图或超大照片按原始全尺寸解码。
                if (ratio >= viewportWidth / viewportHeight)
                    bitmap.DecodePixelWidth = (int)Math.Clamp(viewportWidth * scale * 2, 256, 8192);
                else bitmap.DecodePixelHeight = (int)Math.Clamp(viewportHeight * scale * 2, 256, 8192);
                await ViewerImageDecodeSlots.WaitAsync(ct);
                try
                {
                    using var stream = await file.OpenReadAsync().AsTask(ct);
                    // 原生解码结束前保留流，切换请求通过完成后的令牌检查丢弃旧图。
                    await bitmap.SetSourceAsync(stream);
                }
                finally { ViewerImageDecodeSlots.Release(); }
                ct.ThrowIfCancellationRequested();

                ViewerImage.Source = bitmap;
                SetImageTransformButtonsEnabled(true);
                ImageScroller.Visibility = Visibility.Visible;
                ViewerState.Message = "";
                FitImageToViewport();
                _ = DiscoverMotionPhotoAsync(item, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && !ct.IsCancellationRequested)
            {
                StopVideo();
                SetImageTransformButtonsEnabled(false);
                ViewerState.Message = AppText.Format("CouldNotOpenThisFileItMayBeDamaged", ex.Message);
            }
        }
    }

    private void StopVideo(bool keepRotationButtonEnabled = false)
    {
        _videoSingleTapPlayer = null;
        _videoMousePressIsLeft = false;
        AudioPresentation.Visibility = Visibility.Collapsed;
        AudioArtwork.Source = null;
        LiveText.SetOnce(AudioTitle, "Text", localized => localized.Text = "");
        LiveText.SetOnce(AudioSubtitle, "Text", localized => localized.Text = "");
        FitVideoWindowButton.IsEnabled = false;
        if (!keepRotationButtonEnabled) RotateMediaButton.IsEnabled = false;
        var player = _videoPlayer;
        _videoPlayer = null;
        DetachVideoControls(player);
        player?.Dispose();
        ViewerVideo.IsFullWindow = false;
        ViewerVideo.Visibility = Visibility.Collapsed;
        ViewerVideo.Opacity = 0;
        ViewerVideo.IsHitTestVisible = false;
    }

    private void CloseViewer() => CloseViewerCore(restoreFocus: true);

    private void CloseViewerCore(bool restoreFocus)
    {
        bool wasVisible = Viewer.Visibility == Visibility.Visible;
        if (!_closed) SetViewerFullScreen(false);
        _panOrigin = null;
        ViewerImage.ReleasePointerCaptures();
        // 返回浏览区时高亮刚才看的那一项，方便接着找。
        string? viewedPath = !IsFilmstrip ? ViewerState.CurrentItem?.Path : null;
        if (viewedPath is not null) SelectItem(ViewerState.CurrentItem);
        ViewerState.Close();
        ResetViewerSidePanel();
        StopVideo();
        ClearViewerImage();
        RestoreBrowseWindowPlacement();
        if (wasVisible && !IsFilmstrip && !_closed) RestoreBrowsePosition(viewedPath);
        if (wasVisible && !_closed && restoreFocus)
        {
            if (_sidebarVisible && (_currentFolderNode ?? FolderTree.SelectedNode) is { } node)
                FocusTreeNode(FolderTree, node, FocusState.Pointer);
            else GalleryScroller.Focus(FocusState.Pointer);
        }
    }

    private void ClearViewerImage(bool keepImageButtonsEnabled = false)
    {
        ResetMotionPhoto();
        // GIF 自带播放时钟，切换后立即停止，避免等待对象回收才释放动画资源。
        if (ViewerImage.Source is BitmapImage bitmap) bitmap.Stop();
        ViewerImage.Source = null;
        _viewerDetails = null;
        _imageFitMode = true;
        _imageFitPending = true;
        UpdateZoomIndicators();
        ResetViewerRotation(keepImageButtonsEnabled);
    }

    private void ViewerFullScreen_Click(object sender, RoutedEventArgs e) =>
        SetViewerFullScreen(_presenterBeforeFullScreen is null);

    private void SetViewerFullScreen(bool fullScreen)
    {
        if (fullScreen == (_presenterBeforeFullScreen is not null)) return;
        if (fullScreen)
        {
            var previous = AppWindow.Presenter;
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _presenterBeforeFullScreen = previous;
            AppTitleBar.Visibility = Visibility.Collapsed;
            Grid.SetRow(Viewer, 0);
            Grid.SetRowSpan(Viewer, 3);
        }
        else
        {
            AppWindow.SetPresenter(_presenterBeforeFullScreen!);
            _presenterBeforeFullScreen = null;
            AppTitleBar.Visibility = Visibility.Visible;
            Grid.SetRow(Viewer, 1);
            Grid.SetRowSpan(Viewer, 2);
        }
        ViewerFullScreenIcon.Glyph = FluentIcons.Glyph(fullScreen ? "IconExitFullScreen" : "IconFullScreen");
        ResetFullScreenControls();
        PlaceViewer();
        UpdateViewerOverlays();
        string label = fullScreen ? AppText.Get("ExitFullScreen") : AppText.Get("FullScreen");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ViewerFullScreenButton, label);
        LiveText.SetOnce(ViewerFullScreenButton, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, $"{label} (F11)"));
    }

    /// <summary>尺寸由 Viewbox 跟随视口约束，切图和窗口变化时只重置缩放。</summary>
    private void FitImageToViewport()
    {
        _imageFitMode = true;
        _imageFitPending = true;
        UpdateZoomIndicators();
        if (ImageScroller.ActualWidth <= 0 || ImageScroller.ActualHeight <= 0) return; // 还没完成布局，等 SizeChanged
        ImageScroller.ChangeView(0, 0, 1f, disableAnimation: true);
        if (Math.Abs(ImageScroller.ZoomFactor - 1) <= 0.0001) _imageFitPending = false;
    }

    private void ImageScroller_SizeChanged(object sender, SizeChangedEventArgs e) => FitImageToViewport();
    private void ViewerImage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse
            || ImageScroller.ZoomFactor <= 1 || !e.GetCurrentPoint(ViewerImage).Properties.IsLeftButtonPressed) return;
        if (!ViewerImage.CapturePointer(e.Pointer)) return;
        _panOrigin = e.GetCurrentPoint(Root).Position;
        _panHorizontalOffset = ImageScroller.HorizontalOffset;
        _panVerticalOffset = ImageScroller.VerticalOffset;
        e.Handled = true;
    }

    private void ViewerImage_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_panOrigin is not { } origin) return;
        var point = e.GetCurrentPoint(Root).Position;
        ImageScroller.ChangeView(_panHorizontalOffset + origin.X - point.X,
            _panVerticalOffset + origin.Y - point.Y, null, disableAnimation: true);
        e.Handled = true;
    }

    private void ViewerImage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _panOrigin = null;
        ViewerImage.ReleasePointerCapture(e.Pointer);
    }

    private void ImageScroller_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (!ViewerState.IsOpen || ViewerState.CurrentItem?.IsImage != true || ViewerImage.Source is null) return;
        _panOrigin = null;
        ViewerImage.ReleasePointerCaptures();
        if (ImageScroller.ZoomFactor > 1.0001f) FitImageToViewport();
        else ZoomTo(2);
    }
    private void CloseViewer_Click(object sender, RoutedEventArgs e) => CloseViewer();
    private async void Prev_Click(object sender, RoutedEventArgs e) => await ShowInViewerAsync(ViewerState.CurrentIndex - 1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await ShowInViewerAsync(ViewerState.CurrentIndex + 1);

    // ───────────── 键盘 ─────────────

    private async void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_closed || _closing) { e.Handled = true; return; }
        if (_marqueeStarted)
        {
            if (e.Key == VirtualKey.Escape) EndMarqueeSelection(cancel: true);
            e.Handled = true;
            return;
        }
        if (_settingsDialog is not null) return;
        bool control = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        bool alt = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (_searchDialog is not null) return;
        if (control && !alt && !IsShiftDown() && e.Key == VirtualKey.N)
        {
            e.Handled = true;
            RequestNewWindow(GalleryState.CurrentFolder);
            return;
        }
        if (await HandleExplorerKeyAsync(e, control, alt))
        { e.Handled = true; return; }
        if (control && !alt && e.Key == VirtualKey.F)
        {
            e.Handled = true;
            await ShowSearchAsync();
            return;
        }
        if (control && !alt && !IsShiftDown() && e.Key is VirtualKey.C or VirtualKey.V or VirtualKey.X)
        {
            if (_deleting || _pickingFolder || _itemMenu is not null || _addressFlyout is not null
                || IsClipboardTextTarget(e.OriginalSource as DependencyObject)) return;
            e.Handled = true;
            await HandleFileClipboardKeyAsync(e.Key, e.OriginalSource as DependencyObject);
            return;
        }
        if (!ViewerState.IsOpen || IsFilmstrip)
        {
            if ((control && e.Key == VirtualKey.L) || (alt && e.Key == VirtualKey.D)
                || (!control && !alt && e.Key == VirtualKey.F4))
            {
                e.Handled = true;
                BeginAddressEdit();
                return;
            }
            if (alt && e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up)
            {
                e.Handled = true;
                if (e.Key == VirtualKey.Left) await GoBackAsync();
                else if (e.Key == VirtualKey.Right) await GoForwardAsync();
                else await GoUpAsync();
                return;
            }
        }
        if (control && e.Key == VirtualKey.E && Viewer.Visibility == Visibility.Visible && ViewerState.CurrentItem?.IsVideo == true)
        {
            e.Handled = true;
            await CaptureVideoFrameAsync();
            return;
        }
        // 与 Windows 照片一致的图片缩放快捷键：Ctrl+0 适应窗口，Ctrl+= / Ctrl+- 放大缩小。
        if (control && Viewer.Visibility == Visibility.Visible && ViewerState.CurrentItem is { IsImage: true }
            && e.Key is VirtualKey.Number0 or VirtualKey.NumberPad0 or (VirtualKey)187 or (VirtualKey)189 or VirtualKey.Add or VirtualKey.Subtract)
        {
            e.Handled = true;
            if (e.Key is VirtualKey.Number0 or VirtualKey.NumberPad0) FitImageToViewport();
            else ZoomBy(e.Key is (VirtualKey)187 or VirtualKey.Add ? 1.25 : 1 / 1.25);
            return;
        }
        if (e.Key != VirtualKey.Escape
            && ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                | Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu))
                & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0) return;
        if (_addressFlyout is not null || _itemMenu is not null) return;
        if (e.Key == VirtualKey.Escape && _videoVolumeFlyoutOpen)
        {
            e.Handled = true;
            VideoVolumeFlyout.Hide();
            return;
        }
        // 不抢占输入框、主题下拉框及播放进度/音量滑块的按键。
        for (var element = e.OriginalSource as DependencyObject; element is not null;
             element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
            if (element is TextBox or ComboBox || (e.Key != VirtualKey.Escape && element is Slider)) return;

        if (Viewer.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case VirtualKey.F11:
                    e.Handled = true;
                    SetViewerFullScreen(_presenterBeforeFullScreen is null);
                    break;
                case VirtualKey.Escape:
                    e.Handled = true;
                    if (IsFilmstrip && _presenterBeforeFullScreen is not null) SetViewerFullScreen(false);
                    else if (IsFilmstrip) { await SetDisplayModeAsync(GalleryDisplayMode.Masonry); await ReturnToFolderTreeAsync(); }
                    else await CloseViewerAndReturnToTreeAsync();
                    break;
                case VirtualKey.Delete:
                    e.Handled = true;
                    await DeleteFocusedItemAsync(IsShiftDown(), e.OriginalSource as DependencyObject);
                    break;
                case VirtualKey.Space when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    ToggleVideoPlayback();
                    break;
                case VirtualKey.M when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    ToggleMute();
                    break;
                // 视频快捷键尽量对齐 PotPlayer；逐帧用 , . 以免和横排模式的 A/D 目录跳转冲突。
                case VirtualKey.Enter:
                    e.Handled = true;
                    SetViewerFullScreen(_presenterBeforeFullScreen is null);
                    break;
                case VirtualKey.I:
                    e.Handled = true;
                    SetDetailsOpen(!_detailsOpen);
                    break;
                case VirtualKey.Up when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    ChangeVolume(5);
                    break;
                case VirtualKey.Down when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    ChangeVolume(-5);
                    break;
                case VirtualKey.C when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    ChangeRate(1);
                    break;
                case VirtualKey.X when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    ChangeRate(-1);
                    break;
                case VirtualKey.Z when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    ChangeRate(0);
                    break;
                case (VirtualKey)188 when ViewerState.CurrentItem?.IsVideo == true:
                    e.Handled = true;
                    StepVideoFrame(false);
                    break;
                case (VirtualKey)190 when ViewerState.CurrentItem?.IsVideo == true:
                    e.Handled = true;
                    StepVideoFrame(true);
                    break;
                case (VirtualKey)219 when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    SetLoopPoint(true);
                    break;
                case (VirtualKey)221 when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    SetLoopPoint(false);
                    break;
                case (VirtualKey)220 when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    ClearLoop();
                    break;
                case VirtualKey.L when !IsFilmstrip:
                    e.Handled = true;
                    SetPlaylistOpen(!_playlistOpen);
                    break;
                case VirtualKey.Left when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    SeekVideo(TimeSpan.FromSeconds(-5));
                    break;
                case VirtualKey.Right when ViewerState.CurrentItem?.IsPlaybackMedia == true:
                    e.Handled = true;
                    SeekVideo(TimeSpan.FromSeconds(5));
                    break;
                case VirtualKey.Left:
                case VirtualKey.PageUp:
                    e.Handled = true;
                    await ShowInViewerAsync(ViewerState.CurrentIndex - 1);
                    break;
                case VirtualKey.Right:
                case VirtualKey.PageDown:
                    e.Handled = true;
                    await ShowInViewerAsync(ViewerState.CurrentIndex + 1);
                    break;
            }
            if (e.Handled || !IsFilmstrip) return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                if (_gesturePointer is not null || GalleryState.IsSearching)
                {
                    e.Handled = true;
                    CancelGesture();
                    GalleryState.CancelSearch();
                }
                break;
            case VirtualKey.Delete:
                e.Handled = true;
                await DeleteFocusedItemAsync(IsShiftDown(), e.OriginalSource as DependencyObject);
                break;
            case VirtualKey.F5:
                if (GalleryState.CurrentFolder is not null)
                {
                    e.Handled = true;
                    await NavigateAsync(GalleryState.CurrentFolder, refresh: true);
                }
                break;
            case VirtualKey.W:
                e.Handled = true;
                await GoUpAsync();
                break;
            case VirtualKey.S:
                e.Handled = true;
                await GoBackAsync();
                break;
            case VirtualKey.A:
                e.Handled = true;
                await GoAdjacentFolderAsync(false);
                break;
            case VirtualKey.D:
                e.Handled = true;
                await GoAdjacentFolderAsync(true);
                break;
        }
    }
}
