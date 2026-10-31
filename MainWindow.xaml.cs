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
    private MediaPlayer? _videoPlayer;
    private MediaSource? _videoSource;
    private AppWindowPresenter? _presenterBeforeFullScreen;
    private bool _initialized;
    private bool _closed;
    private bool _pickingFolder;
    private Windows.Foundation.Point? _panOrigin;
    private double _panHorizontalOffset;
    private double _panVerticalOffset;

    public MainWindow(AppSettingsStore? settingsStore = null)
    {
        if (settingsStore is not null) _settingsStore = settingsStore;
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fallery.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Root.ActualThemeChanged += Root_ActualThemeChanged;
        AppTitleBar.ActualThemeChanged += Root_ActualThemeChanged;
        Root.Loaded += Root_Loaded;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
        ThemeSelector.SelectedIndex = 0;
        InitializeExperience();
        InitializeViewModels();
        RefreshAddressBar();
    }

    // 系统标题栏不属于 XAML 树，需要和页面主题分别同步。
    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is ComboBoxItem { Tag: string theme }
            && Enum.TryParse<ElementTheme>(theme, out var requestedTheme))
        {
            Root.RequestedTheme = requestedTheme;
            UpdateTitleBarTheme();
            QueueSettingsSave();
        }
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateTitleBarTheme();
        if (_initialized) return;
        _initialized = true;
        await RestoreSettingsAsync();
        if (_closed) return;
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
            foreach (var entry in roots)
                FolderTree.RootNodes.Add(CreateNode(entry.Path, entry.Name));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed) GalleryState.Status = $"无法读取磁盘：{ex.Message}";
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
        _closed = true;
        CloseAddressMenu();
        GalleryState.Dispose();
        ViewerState.Close();
        _lifetimeCts.Cancel();
        GalleryState.CancelSearch();
        _settingsTimer.Stop();
        CancelGesture();
        CloseViewer();
        ReleaseGalleryThumbnails();
        MediaCache.Clear();
    }

    private static TreeViewNode CreateNode(string path, string name) => new()
    {
        Content = new FolderEntry(path, name),
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
        var children = await Task.Run(() => FolderScanner.GetChildFolders(entry.Path, _lifetimeCts.Token));
        _lifetimeCts.Token.ThrowIfCancellationRequested();
        foreach (var (path, hasChildren) in children)
        {
            var child = CreateNode(path, System.IO.Path.GetFileName(path));
            child.HasUnrealizedChildren = hasChildren;
            node.Children.Add(child);
        }
        node.HasUnrealizedChildren = false;
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
            // “图片”与盘符可能指向同一目录树，优先使用最接近目标的根节点。
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
                    ancestor = System.IO.Path.Combine(ancestor, segment);
                    var child = node.Children.FirstOrDefault(n => n.Content is FolderEntry entry
                        && string.Equals(NormalizeFolderPath(entry.Path), ancestor, StringComparison.OrdinalIgnoreCase));
                    if (child is null)
                    {
                        // 显式打开的隐藏目录或扫描后新建目录也需要可定位。
                        child = CreateNode(ancestor, segment);
                        node.Children.Add(child);
                    }
                    node.IsExpanded = true;
                    node = child;
                }
            }
            ct.ThrowIfCancellationRequested();
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
                GalleryState.Status += $" · 无法定位文件树：{ex.Message}";
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
        if (args.InvokedItem is TreeViewNode { Content: FolderEntry entry }) await NavigateAsync(entry.Path);
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
            if (!_closed) GalleryState.Status = $"无法打开文件夹选择器：{ex.Message}";
        }
        finally { _pickingFolder = false; }
        if (!_closed && path is not null) await NavigateAsync(path);
    }

    private Task NavigateAsync(string folder, bool pushHistory = true, bool refresh = false) =>
        NavigateCoreAsync(folder, pushHistory, refresh, forward: false);

    private async Task NavigateCoreAsync(string folder, bool pushHistory, bool refresh, bool forward)
    {
        if (_closed || _closing) return;
        _navigationVersion++;
        if (_addressRequest != _navigationVersion && AddressEditor.Visibility == Visibility.Visible)
            EndAddressEdit(cancelRequest: false);
        CloseAddressMenu();
        CancelGesture();
        if (refresh || GalleryState.IsScanning || !string.Equals(NormalizeFolderPath(folder), GalleryState.CurrentFolder, StringComparison.OrdinalIgnoreCase))
            CloseViewer();
        if (!await GalleryState.NavigateAsync(folder, pushHistory, refresh, forward) && !_closed && !GalleryState.IsScanning)
            FolderTree.SelectedNode = _currentFolderNode;
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
        EmptyText.Visibility = GalleryState.IsScanning || GalleryState.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = GalleryState.IsScanning ? "正在读取目录…" : GalleryState.CurrentFolder is null
            ? "在左侧选择目录，或点击“打开文件夹”" : "此目录没有可显示的文件夹、图片或视频";
    }

    private uint ThumbnailDecodeSize
    {
        get
        {
            double scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
            return (uint)Math.Clamp(ThumbSize.Value * scale, 128, 1024);
        }
    }

    private void Gallery_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Index < 0 || args.Index >= GalleryState.Items.Count) return;
        var item = GalleryState.Items[args.Index];
        if (args.Element is MediaTile tile) tile.Bind(item);
        _realizedItems[args.Element] = item;
        _ = item.EnsureThumbnailAsync(ThumbnailDecodeSize);
    }

    private void Gallery_ElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is MediaTile tile) tile.Bind(null);
        if (_realizedItems.Remove(args.Element, out var item)) item.ReleaseThumbnail();
    }

    private async void Tile_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is UIElement element && _realizedItems.TryGetValue(element, out var item))
        {
            e.Handled = true;
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

    private async Task ShowInViewerAsync(int index)
    {
        if (GalleryState.IsScanning || index < 0 || index >= GalleryState.MediaItems.Count || _closed || _closing) return;
        var request = ViewerState.BeginOpen(index);
        if (request is null) return;
        var ct = request.Token;
        var item = request.Item;

        _panOrigin = null;
        ViewerImage.ReleasePointerCaptures();
        StopVideo();
        ClearViewerImage();
        ImageScroller.Visibility = item.IsVideo ? Visibility.Collapsed : Visibility.Visible;
        CloseViewerButton.Focus(FocusState.Programmatic);

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.Path).AsTask(ct);
            ct.ThrowIfCancellationRequested();

            if (item.IsVideo)
            {
                var player = new MediaPlayer { AutoPlay = true };
                _videoPlayer = player;
                player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_closed && ReferenceEquals(_videoPlayer, player)) ViewerState.Message = "";
                });
                player.MediaFailed += (_, args) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (_closed || !ReferenceEquals(_videoPlayer, player)) return;
                    ViewerState.Message = $"无法播放此视频，文件可能损坏或系统缺少解码器。\n{args.ErrorMessage}";
                    StopVideo();
                });
                _videoSource = MediaSource.CreateFromStorageFile(file);
                ViewerVideo.SetMediaPlayer(player);
                ViewerVideo.Visibility = Visibility.Visible;
                player.Source = _videoSource;
            }
            else
            {
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
                using (var stream = await file.OpenReadAsync().AsTask(ct))
                {
                    await bitmap.SetSourceAsync(stream).AsTask(ct);
                }
                ct.ThrowIfCancellationRequested();

                ViewerImage.Source = bitmap;
                ImageScroller.Visibility = Visibility.Visible;
                ViewerState.Message = "";
                FitImageToViewport();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && !ct.IsCancellationRequested)
            {
                StopVideo();
                ViewerState.Message = $"无法打开此文件，文件可能损坏或系统缺少解码器。\n{ex.Message}";
            }
        }
    }

    private void StopVideo()
    {
        var player = _videoPlayer;
        _videoPlayer = null;
        if (player is not null)
        {
            player.Pause();
            player.Source = null;
            ViewerVideo.SetMediaPlayer(null);
            player.Dispose();
        }
        _videoSource?.Dispose();
        _videoSource = null;
        ViewerVideo.IsFullWindow = false;
        ViewerVideo.Visibility = Visibility.Collapsed;
    }

    private void CloseViewer()
    {
        bool wasVisible = Viewer.Visibility == Visibility.Visible;
        if (!_closed) SetViewerFullScreen(false);
        _panOrigin = null;
        ViewerImage.ReleasePointerCaptures();
        ViewerState.Close();
        StopVideo();
        ClearViewerImage();
        if (wasVisible && !_closed)
        {
            if (_sidebarVisible) FolderTree.Focus(FocusState.Programmatic);
            else GalleryScroller.Focus(FocusState.Programmatic);
        }
    }

    private void ClearViewerImage()
    {
        // GIF 自带播放时钟，切换后立即停止，避免等待对象回收才释放动画资源。
        if (ViewerImage.Source is BitmapImage bitmap) bitmap.Stop();
        ViewerImage.Source = null;
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
        ViewerFullScreenIcon.Glyph = fullScreen ? "\uE73F" : "\uE740";
        FullScreenExitButton.Visibility = fullScreen ? Visibility.Visible : Visibility.Collapsed;
        string label = fullScreen ? "退出全屏" : "全屏";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ViewerFullScreenButton, label);
        ToolTipService.SetToolTip(ViewerFullScreenButton, $"{label} (F11)");
    }

    /// <summary>尺寸由 Viewbox 跟随视口约束，切图和窗口变化时只重置缩放。</summary>
    private void FitImageToViewport()
    {
        if (ImageScroller.ActualWidth <= 0 || ImageScroller.ActualHeight <= 0) return; // 还没完成布局，等 SizeChanged
        ImageScroller.ChangeView(0, 0, 1f, disableAnimation: true);
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
        CloseViewer();
    }
    private void CloseViewer_Click(object sender, RoutedEventArgs e) => CloseViewer();
    private async void Prev_Click(object sender, RoutedEventArgs e) => await ShowInViewerAsync(ViewerState.CurrentIndex - 1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await ShowInViewerAsync(ViewerState.CurrentIndex + 1);

    // ───────────── 键盘 ─────────────

    private async void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool control = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        bool alt = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (!ViewerState.IsOpen)
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
        if (e.Key != VirtualKey.Escape
            && ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                | Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu))
                & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0) return;
        if (_addressFlyout is not null) return;
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
                    CloseViewer();
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
            return;
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
