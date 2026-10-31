using Fallery.Layouts;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Storage;
using Windows.System;

namespace Fallery;

public sealed partial class MainWindow : Window
{
    private const int ProbeBatchSize = 200;

    private List<MediaItem> _items = [];
    private readonly Stack<string> _history = new();
    private string? _currentFolder;
    private CancellationTokenSource? _loadCts;
    private int _viewerIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fallery.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Root.ActualThemeChanged += Root_ActualThemeChanged;
        Root.Loaded += Root_Loaded;
        Activated += MainWindow_Activated;
        ThemeSelector.SelectedIndex = 0;
        LoadTreeRoots();
    }

    // 系统标题栏不属于 XAML 树，需要和页面主题分别同步。
    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is ComboBoxItem { Tag: string theme }
            && Enum.TryParse<ElementTheme>(theme, out var requestedTheme))
        {
            Root.RequestedTheme = requestedTheme;
            UpdateTitleBarTheme();
        }
    }

    private void Root_Loaded(object sender, RoutedEventArgs e) => UpdateTitleBarTheme();
    private void Root_ActualThemeChanged(FrameworkElement sender, object args) => UpdateTitleBarTheme();
    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args) => UpdateTitleBarTheme();

    private void UpdateTitleBarTheme()
    {
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        var titleBar = AppWindow.TitleBar;
        // 系统窗口按钮不继承 RequestedTheme，使用 SDK 的标题栏主题接口同步。
        titleBar.PreferredTheme = dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        var background = dark ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32)
            : Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243);
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

    private void LoadTreeRoots()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (Directory.Exists(pictures)) FolderTree.RootNodes.Add(CreateNode(pictures, "图片"));

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var label = string.IsNullOrEmpty(drive.VolumeLabel) ? drive.Name : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
            FolderTree.RootNodes.Add(CreateNode(drive.RootDirectory.FullName, label));
        }
    }

    private static TreeViewNode CreateNode(string path, string name) => new()
    {
        Content = new FolderEntry(path, name),
        HasUnrealizedChildren = true,
    };

    private async void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        var node = args.Node;
        if (!node.HasUnrealizedChildren || node.Content is not FolderEntry entry) return;
        node.HasUnrealizedChildren = false;

        // 网络盘可能很慢，放到后台
        var children = await Task.Run(() => FolderScanner.GetSubfolders(entry.Path)
            .Select(p => (Path: p, HasChildren: FolderScanner.HasSubfolders(p)))
            .ToList());

        foreach (var (path, hasChildren) in children)
        {
            var child = CreateNode(path, System.IO.Path.GetFileName(path));
            child.HasUnrealizedChildren = hasChildren;
            node.Children.Add(child);
        }
    }

    private async void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode { Content: FolderEntry entry }) await NavigateAsync(entry.Path);
    }

    // ───────────── 导航 ─────────────

    private async Task NavigateAsync(string folder, bool pushHistory = true)
    {
        if (string.Equals(folder, _currentFolder, StringComparison.OrdinalIgnoreCase)) return;

        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();

        if (pushHistory && _currentFolder is not null) _history.Push(_currentFolder);
        _currentFolder = folder;
        UpdateBreadcrumb(folder);
        StatusText.Text = "正在扫描…";

        List<MediaItem> items;
        try
        {
            items = await Task.Run(() => FolderScanner.Scan(folder, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            StatusText.Text = $"无法打开这个文件夹：{ex.Message}";
            return;
        }

        _items = items;
        Gallery.ItemsSource = _items;
        GalleryScroller.ChangeView(null, 0, null, disableAnimation: true);

        int videos = items.Count(i => i.IsVideo);
        StatusText.Text = $"{items.Count - videos} 张图片 · {videos} 个视频";

        await ProbeAspectRatiosAsync(items, cts.Token);
    }

    /// <summary>后台读文件头拿宽高比，每批刷新一次瀑布流。</summary>
    private async Task ProbeAspectRatiosAsync(List<MediaItem> items, CancellationToken ct)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct };
        try
        {
            foreach (var batch in items.Chunk(ProbeBatchSize))
            {
                await Parallel.ForEachAsync(batch, options, async (item, _) =>
                {
                    if (await MediaProbe.TryGetAspectRatioAsync(item) is double ratio) item.AspectRatio = ratio;
                });
                ((MasonryLayout)Gallery.Layout).Refresh(); // await 之后回到 UI 线程
            }
        }
        catch (OperationCanceledException)
        {
            // 切换了目录
        }
    }

    private void UpdateBreadcrumb(string folder)
    {
        var segments = new List<FolderEntry>();
        for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
        {
            var name = dir.Parent is null ? dir.FullName.TrimEnd('\\') : dir.Name;
            segments.Insert(0, new FolderEntry(dir.FullName, name));
        }
        Breadcrumb.ItemsSource = segments;
    }

    private async void Breadcrumb_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is FolderEntry entry) await NavigateAsync(entry.Path);
    }

    private async void Back_Click(object sender, RoutedEventArgs e) => await GoBackAsync();
    private async void Up_Click(object sender, RoutedEventArgs e) => await GoUpAsync();

    private async Task GoBackAsync()
    {
        if (_history.TryPop(out var previous)) await NavigateAsync(previous, pushHistory: false);
    }

    private async Task GoUpAsync()
    {
        if (_currentFolder is not null && Directory.GetParent(_currentFolder) is { } parent) await NavigateAsync(parent.FullName);
    }

    // ───────────── 瀑布流 ─────────────

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
        if (args.Index >= 0 && args.Index < _items.Count) _ = _items[args.Index].EnsureThumbnailAsync(ThumbnailDecodeSize);
    }

    private void Gallery_ElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if ((args.Element as FrameworkElement)?.DataContext is MediaItem item) item.ReleaseThumbnail();
    }

    private async void Tile_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MediaItem item) await ShowInViewerAsync(_items.IndexOf(item));
    }

    // ───────────── 查看器 ─────────────

    private async Task ShowInViewerAsync(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        _viewerIndex = index;
        var item = _items[index];

        Viewer.Visibility = Visibility.Visible;
        ViewerTitle.Text = item.Name;
        ViewerCounter.Text = $"{index + 1} / {_items.Count}";

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.Path);
            if (index != _viewerIndex) return; // 用户已经切到别的项

            if (item.IsVideo)
            {
                ImageScroller.Visibility = Visibility.Collapsed;
                ViewerImage.Source = null;
                StopVideo();
                ViewerVideo.Source = MediaSource.CreateFromStorageFile(file);
                ViewerVideo.Visibility = Visibility.Visible;
            }
            else
            {
                StopVideo();
                var bitmap = new BitmapImage();
                using (var stream = await file.OpenReadAsync())
                {
                    await bitmap.SetSourceAsync(stream);
                }
                if (index != _viewerIndex) return;

                ViewerImage.Source = bitmap;
                ImageScroller.Visibility = Visibility.Visible;
                FitImageToViewport();
            }
        }
        catch (Exception ex)
        {
            ViewerTitle.Text = $"{item.Name}（无法打开：{ex.Message}）";
        }
    }

    private void StopVideo()
    {
        var source = ViewerVideo.Source as MediaSource;
        ViewerVideo.MediaPlayer?.Pause();
        ViewerVideo.Source = null;
        source?.Dispose();
        ViewerVideo.Visibility = Visibility.Collapsed;
    }

    private void CloseViewer()
    {
        StopVideo();
        ViewerImage.Source = null;
        Viewer.Visibility = Visibility.Collapsed;
        _viewerIndex = -1;
    }

    /// <summary>Image 尺寸等于视口时 Stretch=Uniform 即“适应窗口”，ScrollViewer 的缩放在此基础上进行。</summary>
    private void FitImageToViewport()
    {
        if (ImageScroller.ActualWidth <= 0 || ImageScroller.ActualHeight <= 0) return; // 还没完成布局，等 SizeChanged
        ViewerImage.Width = ImageScroller.ActualWidth;
        ViewerImage.Height = ImageScroller.ActualHeight;
        ImageScroller.ChangeView(0, 0, 1f, disableAnimation: true);
    }

    private void ImageScroller_SizeChanged(object sender, SizeChangedEventArgs e) => FitImageToViewport();
    private void ImageScroller_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => CloseViewer();
    private void CloseViewer_Click(object sender, RoutedEventArgs e) => CloseViewer();
    private async void Prev_Click(object sender, RoutedEventArgs e) => await ShowInViewerAsync(_viewerIndex - 1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await ShowInViewerAsync(_viewerIndex + 1);

    // ───────────── 键盘 ─────────────

    private async void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Viewer.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case VirtualKey.Escape:
                    e.Handled = true;
                    CloseViewer();
                    break;
                case VirtualKey.Left:
                case VirtualKey.PageUp:
                    e.Handled = true;
                    await ShowInViewerAsync(_viewerIndex - 1);
                    break;
                case VirtualKey.Right:
                case VirtualKey.PageDown:
                    e.Handled = true;
                    await ShowInViewerAsync(_viewerIndex + 1);
                    break;
            }
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.W:
                e.Handled = true;
                await GoUpAsync();
                break;
            case VirtualKey.S:
                e.Handled = true;
                await GoBackAsync();
                break;
            // A / D：上一个/下一个含图片的文件夹，阶段 4 再做
        }
    }
}
