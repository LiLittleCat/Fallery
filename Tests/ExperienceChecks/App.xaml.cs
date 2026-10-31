using System.Reflection;
using System.Runtime.InteropServices;
using Fallery.Models;
using Fallery.Services;
using Fallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Fallery.ExperienceChecks;

public partial class App : Application
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    public App()
    {
        UnhandledException += (_, e) => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "experience-checks.txt"), $"\nFAIL: {e.Exception}");
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args) => _ = RunAsync();

    private async Task RunAsync()
    {
        string report = Path.Combine(AppContext.BaseDirectory, "experience-checks.txt");
        File.WriteAllText(report, "RUNNING");
        MainWindow? window = null;
        try
        {
            int checks = 0;
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
            var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../.."));
            var settingsPath = Path.Combine(workspace, "work", "validation", "experience-checks", "settings.json");
            var store = new AppSettingsStore(settingsPath);
            await store.SaveAsync(new AppSettings { Width = 1100, Height = 720, VideoWindowDefaultsApplied = true, Theme = 2, ThumbnailSize = 320,
                SidebarWidth = 300, SortField = MediaSortField.Size, Descending = true });
            // 直接实例化生产窗口，不激活、不发送输入，设置写入隔离的测试目录。
            window = new MainWindow(store, monitorFolderChanges: false);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object? Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
            void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
            async Task Invoke(string method, params object[] arguments) => await ((Task)typeof(MainWindow).GetMethod(method, flags)!.Invoke(window, arguments)!).WaitAsync(TimeSpan.FromSeconds(15));
            Set("_initialized", true);
            var bindings = Get("Bindings")!;
            bindings.GetType().GetMethod("Initialize")!.Invoke(bindings, null);
            await Invoke("RestoreSettingsAsync");
            Check((bool)Get("_settingsReady")!, "实际窗口没有完成设置恢复");
            var root = (FrameworkElement)window.Content;
            // 未激活的宿主无法显示滑块数值浮层；这里只检查值恢复，交互提示留给人工验收。
            ((Slider)root.FindName("ThumbSize")).IsThumbToolTipEnabled = false;
            root.Measure(new Windows.Foundation.Size(1100, 720));
            root.Arrange(new Windows.Foundation.Rect(0, 0, 1100, 720));
            root.UpdateLayout();
            Check((int)Get("_themeIndex")! == 2, "实际主题控件没有恢复");
            Check(((Slider)root.FindName("ThumbSize")).Value == 320, "实际缩略图控件没有恢复");
            var folder = Path.Combine(workspace, "work", "validation", "experience", "01-图片");
            await Invoke("NavigateAsync", folder, false, false);
            await Invoke("SelectCurrentFolderAsync", folder, CancellationToken.None);
            var items = window.GalleryState.Items;
            root.UpdateLayout();
            var tree = (TreeView)root.FindName("FolderTree");
            var firstNode = tree.RootNodes[0];
            var container = tree.ContainerFromNode(firstNode);
            Check(container is not null && Descendants(container).OfType<TextBlock>().Any(t => t.Text == ((FolderEntry)firstNode.Content).Name),
                "文件树模板没有显示真实节点名称");
            typeof(MainWindow).GetMethod("SidebarToggle_Click", flags)!.Invoke(window, new object[] { root, new RoutedEventArgs() });
            root.UpdateLayout();
            var hiddenSettings = (AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!;
            Check(((Grid)root.FindName("SidebarPanel")).Visibility == Visibility.Collapsed && hiddenSettings.SidebarWidth == 300,
                "隐藏文件树不能丢失已保存的侧栏宽度");
            typeof(MainWindow).GetMethod("SidebarToggle_Click", flags)!.Invoke(window, new object[] { root, new RoutedEventArgs() });
            root.UpdateLayout();
            Check(((Grid)root.FindName("SidebarPanel")).Visibility == Visibility.Visible && ((ColumnDefinition)Get("SidebarColumn")!).ActualWidth == 300,
                "恢复文件树应恢复原宽度");
            Check(((TextBlock)root.FindName("StatusText")).Text == window.GalleryState.Status, "状态栏的 x:Bind 未跟随 GalleryViewModel 更新");
            Check(items.Count == 2 && items[0].File.Version!.Value.Length >= items[1].File.Version!.Value.Length, "实际导航未按已保存的大小降序排列");
            var segments = (StackPanel)root.FindName("AddressSegments");
            Check(segments.Children.Count == window.GalleryState.Breadcrumbs.Count * 2
                && segments.Children.OfType<Button>().All(b => b.Tag is FolderEntry), "地址每一级应有独立跳转与子目录菜单入口");
            var address = (TextBox)root.FindName("AddressEditor");
            void BeginAddressEdit() => typeof(MainWindow).GetMethod("BeginAddressEdit", flags)!.Invoke(window, null);
            BeginAddressEdit();
            Check(address.Visibility == Visibility.Visible && address.Text == folder && address.SelectedText == folder,
                "编辑地址时必须显示并全选完整路径，供系统文本框复制粘贴");
            address.Text = Path.Combine(folder, "missing-directory");
            await Invoke("SubmitAddressAsync");
            Check(window.GalleryState.CurrentFolder == folder && address.Visibility == Visibility.Visible && address.Text.EndsWith("missing-directory"),
                "地址不存在时应保留当前目录和输入，允许修改重试");
            var addressDestination = Path.Combine(workspace, "work", "validation", "experience", "10-混合");
            address.Text = $"\"{addressDestination}\"";
            await Invoke("SubmitAddressAsync");
            Check(window.GalleryState.CurrentFolder == addressDestination && address.Visibility == Visibility.Collapsed,
                "提交有效地址后必须导航并恢复路径按钮");
            await Invoke("GoBackAsync");
            Check(window.GalleryState.CurrentFolder == folder && ((Button)root.FindName("ForwardButton")).IsEnabled,
                "后退成功后应启用前进按钮");
            await Invoke("GoForwardAsync");
            Check(window.GalleryState.CurrentFolder == addressDestination && !((Button)root.FindName("ForwardButton")).IsEnabled,
                "前进按钮应沿历史返回并正确更新边界状态");
            await Invoke("GoBackAsync");
            BeginAddressEdit();
            address.Text = "discard this edit";
            typeof(MainWindow).GetMethod("EndAddressEdit", flags)!.Invoke(window, new object[] { true });
            Check(address.Text == folder && address.Visibility == Visibility.Collapsed && window.GalleryState.CanGoForward,
                "取消地址编辑不能改变目录或破坏前进历史");
            var original = items.ToHashSet();
            var galleryControl = (ItemsRepeater)root.FindName("Gallery");
            await Invoke("SetDisplayModeAsync", GalleryDisplayMode.LargeIcons);
            root.UpdateLayout();
            var firstLarge = (Fallery.Controls.MediaTile)galleryControl.TryGetElement(0);
            Check(firstLarge is not null && Math.Abs(firstLarge.ActualHeight - firstLarge.ActualWidth - 22) < 0.01,
                "大图列表应使用方形预览和独立标题行，不能继续沿用照片宽高比");
            var namesToggle = (ToggleMenuFlyoutItem)root.FindName("ShowFileNamesItem");
            namesToggle.IsChecked = false;
            typeof(MainWindow).GetMethod("ShowFileNames_Click", flags)!.Invoke(window, new object[] { namesToggle, new RoutedEventArgs() });
            root.UpdateLayout();
            var nameless = (Fallery.Controls.MediaTile)galleryControl.TryGetElement(0);
            Check(((Grid)nameless.FindName("MediaCaption")).Visibility == Visibility.Collapsed
                && ((RowDefinition)nameless.FindName("CaptionRow")).Height.Value == 0,
                "隐藏文件名必须同时移除标题占位高度");
            await Invoke("SetDisplayModeAsync", GalleryDisplayMode.Filmstrip);
            root.UpdateLayout();
            Check(ReferenceEquals(((Grid)root.FindName("Viewer")).Parent, root.FindName("GalleryContent"))
                && ((Grid)root.FindName("GalleryToolbar")).Visibility == Visibility.Visible
                && galleryControl.Layout is StackLayout { Orientation: Orientation.Horizontal },
                "横排模式应在文件树旁嵌入大图并保留目录导航");
            Check(((Image)root.FindName("ViewerImage")).Source is not null && window.ViewerState.CurrentIndex == 0,
                "切入横排模式后应打开第一张大图");
            Check(((Grid)root.FindName("BrowserSurface")).Opacity == 1
                && ((Border)root.FindName("GallerySurfaceFill")).Opacity == 0,
                "横排查看时保留文件树和缩略图，画面留白应透出系统背景");
            await Invoke("ShowInViewerAsync", 1, false);
            root.UpdateLayout();
            var selectedTile = (Fallery.Controls.MediaTile)galleryControl.TryGetElement(1);
            Check(window.ViewerState.CurrentIndex == 1
                && selectedTile.IsSelected,
                "切换大图后底部缩略图必须同步选中状态");
            var capturedViewSettings = (AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!;
            Check(capturedViewSettings.DisplayMode == GalleryDisplayMode.Filmstrip && !capturedViewSettings.ShowFileNames,
                "窗口保存入口必须读取最新视图与文件名设置");
            await Invoke("NavigateAsync", Path.Combine(folder, "missing-preview-directory"), true, false);
            Check(window.ViewerState.CurrentIndex == 1 && ((Image)root.FindName("ViewerImage")).Source is not null,
                "横排模式跳转失败后必须恢复原目录的大图");
            await Invoke("NavigateAsync", Path.Combine(workspace, "work", "validation", "experience", "02-空目录"), true, false);
            Check(!window.ViewerState.IsOpen && ((Image)root.FindName("ViewerImage")).Source is null
                && ((TextBlock)root.FindName("EmptyText")).Visibility == Visibility.Visible,
                "进入空目录不能残留上一目录的大图");
            await Invoke("GoBackAsync");
            Check(window.ViewerState.CurrentIndex == 0, "横排模式返回目录后应自动打开首项");
            var imageMenu = (MenuFlyout)typeof(MainWindow).GetMethod("BuildItemMenu", flags)!.Invoke(window, new object?[] { items[0].Path, false, null, true })!;
            Check(imageMenu.Items.OfType<MenuFlyoutItem>().Select(i => i.Tag).SequenceEqual(new[] { "open", "reveal", "copy-path", "copy-file", "delete" }),
                "媒体菜单应提供打开、定位、复制和删除入口");
            var treeMenu = (MenuFlyout)typeof(MainWindow).GetMethod("BuildItemMenu", flags)!.Invoke(window, new object?[] { folder, true, firstNode, true })!;
            Check(treeMenu.Items.OfType<MenuFlyoutItem>().Any(i => Equals(i.Tag, "expand"))
                && treeMenu.Items.OfType<MenuFlyoutItem>().Any(i => Equals(i.Tag, "refresh")), "文件树菜单缺少展开或刷新入口");
            Check(treeMenu.Items.OfType<MenuFlyoutItem>().Any(i => Equals(i.Tag, "exclude"))
                && !treeMenu.Items.OfType<MenuFlyoutItem>().Any(i => Equals(i.Tag, "open")), "文件树应以排除替代重复的打开入口");
            await Invoke("SetDisplayModeAsync", GalleryDisplayMode.Masonry);
            namesToggle.IsChecked = true;
            typeof(MainWindow).GetMethod("ShowFileNames_Click", flags)!.Invoke(window, new object[] { namesToggle, new RoutedEventArgs() });
            root.UpdateLayout();
            Check(((Grid)root.FindName("Viewer")).Parent == root && !window.ViewerState.IsOpen
                && galleryControl.Layout is Fallery.Layouts.MasonryLayout,
                "返回瀑布流必须归还查看器覆盖层并恢复原虚拟化布局");
            Check(window.GalleryState.Items.All(original.Contains), "切换视图不能重扫目录或重建媒体模型");
            Check(((Grid)root.FindName("BrowserSurface")).Opacity == 1
                && ((Grid)root.FindName("BrowserSurface")).IsHitTestVisible
                && ((Border)root.FindName("GallerySurfaceFill")).Opacity == 1,
                "关闭查看器后必须恢复浏览区背景和交互");
            window.GalleryState.SortField = MediaSortField.Name;
            window.GalleryState.Descending = false;
            await Invoke("ApplySortAsync");
            items = window.GalleryState.Items;
            Check(items.Select(i => i.Name).SequenceEqual(new[] { "image1.jpg", "image2.jpg" }), "实际排序操作未更新数据顺序");
            Check(items.All(original.Contains), "排序不能重建媒体模型丢掉缓存");
            Check(window.ViewerState.Playlist.SequenceEqual(items), "查看器播放顺序未与排序同步");
            await Invoke("NavigateAsync", folder, false, false);
            await Invoke("SetDisplayModeAsync", GalleryDisplayMode.Masonry);
            window.ViewerState.SetPlaylist([.. window.GalleryState.MediaItems,
                new MediaItem(Path.Combine(workspace, "work", "validation", "media", "video.mp4"), MediaKind.Video)]);
            await Invoke("ShowInViewerAsync", 0, false);
            long wheelTime = 10000;
            await Invoke("NavigateViewerWheelAsync", -60, wheelTime);
            Check(window.ViewerState.CurrentIndex == 0, "高精度滚轮不足一格时不应跳项");
            await Invoke("NavigateViewerWheelAsync", -60, wheelTime + 20);
            Check(window.ViewerState.CurrentIndex == 1, "滚轮向下累计一格应打开下一张图片");
            await Invoke("NavigateViewerWheelAsync", -120, wheelTime + 40);
            Check(window.ViewerState.CurrentIndex == 1, "快速滚轮事件应节流，不能连续跳过多项");
            await Invoke("NavigateViewerWheelAsync", -120, wheelTime + 240);
            Check(window.ViewerState.CurrentItem?.IsVideo == true, "滚轮应从图片切换到视频");
            await Invoke("NavigateViewerWheelAsync", -120, wheelTime + 440);
            Check(window.ViewerState.CurrentIndex == 2, "末项继续向下不能越界或循环");
            await Invoke("NavigateViewerWheelAsync", 120, wheelTime + 640);
            Check(window.ViewerState.CurrentIndex == 1 && window.ViewerState.CurrentItem?.IsVideo == false,
                "滚轮向上应从视频返回上一张图片");
            bool AllowsWheel(bool modified, bool horizontal) => (bool)typeof(MainWindow).GetMethod("CanNavigateViewerWheel", flags)!.Invoke(window, [modified, horizontal])!;
            Check(!AllowsWheel(true, false) && !AllowsWheel(false, true), "组合键缩放及横向滚轮不能触发媒体切换");
            Set("_settingsDialog", new ContentDialog());
            Check(!AllowsWheel(false, false), "弹窗打开时滚轮不能切换底层媒体");
            Set("_settingsDialog", null!);
            await Invoke("SetDisplayModeAsync", GalleryDisplayMode.Filmstrip);
            await Invoke("ShowInViewerAsync", 0, false);
            await Invoke("NavigateViewerWheelAsync", -120, wheelTime + 1000);
            Check(window.ViewerState.CurrentIndex == 1 && ReferenceEquals(Get("_selectedItem"), window.ViewerState.CurrentItem),
                "大图缩略图模式滚轮切换后应同步大图及缩略图选择");
            await Invoke("NavigateViewerWheelAsync", 120, wheelTime + 1200);
            await Invoke("NavigateViewerWheelAsync", 120, wheelTime + 1400);
            Check(window.ViewerState.CurrentIndex == 0, "首项继续向上不能越界或循环");
            typeof(MainWindow).GetMethod("CloseViewer", flags)!.Invoke(window, null);
            Check(!AllowsWheel(false, false), "查看器关闭时不能处理媒体滚轮切换");
            await Invoke("SetDisplayModeAsync", GalleryDisplayMode.Masonry);
            window.ViewerState.SetPlaylist(items);
            typeof(MainWindow).GetMethod("ApplyTheme", flags)!.Invoke(window, new object[] { 1 });
            root.UpdateLayout();
            Check(window.AppWindow.TitleBar.PreferredTheme == Microsoft.UI.Windowing.TitleBarTheme.Light,
                "亮色模式的系统窗口按钮未同步");
            var request = window.ViewerState.BeginOpen(0)!;
            Check(((Grid)root.FindName("Viewer")).Background is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: 0 }
                && ((Grid)root.FindName("BrowserSurface")).Opacity == 0
                && !((Grid)root.FindName("BrowserSurface")).IsHitTestVisible
                && ((Grid)root.FindName("BrowserStatus")).Opacity == 0,
                "覆盖式查看器留白应透明，底下的目录内容不能透出或接收点击");
            Check(window.ViewerState.Title == items[0].Name && window.ViewerState.CanNext && !window.ViewerState.CanPrevious,
                "查看器状态没有从当前项派生");
            Check(((TextBlock)root.FindName("ViewerTitle")).Text == items[0].Name
                && ((Grid)root.FindName("Viewer")).Visibility == Visibility.Visible, "查看器标题或显示状态绑定未更新");
            Check(((Grid)root.FindName("GalleryToolbar")).Visibility == Visibility.Collapsed
                && ((Grid)root.FindName("ViewerToolbar")).Visibility == Visibility.Visible
                && ((TitleBar)root.FindName("AppTitleBar")).RequestedTheme == ElementTheme.Default, "查看器标题栏应继承应用主题");
            root.UpdateLayout();
            var viewerBar = (TitleBar)root.FindName("AppTitleBar");
            Check(viewerBar.Background is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: 0 }
                && viewerBar.ActualTheme == ElementTheme.Light, "查看器标题栏不能遮住系统背景或强制暗色");
            bool PassesThrough(FrameworkElement element)
            {
                var center = element.TransformToVisual(root).TransformPoint(
                    new Windows.Foundation.Point(element.ActualWidth / 2, element.ActualHeight / 2));
                double scale = root.XamlRoot.RasterizationScale;
                return Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id)
                    .GetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough)
                    .Any(rect => center.X * scale >= rect.X && center.X * scale < rect.X + rect.Width
                        && center.Y * scale >= rect.Y && center.Y * scale < rect.Y + rect.Height);
            }
            Check(!PassesThrough((TextBlock)root.FindName("ViewerTitle"))
                && !PassesThrough((TextBlock)root.FindName("ViewerCounter"))
                && PassesThrough((Button)root.FindName("CloseViewerButton")), "查看器文字应可拖动，返回按钮应可点击");
            ((Grid)root.FindName("VideoControls")).Visibility = Visibility.Visible;
            ((StackPanel)root.FindName("ImageZoomPanel")).Visibility = Visibility.Collapsed;
            ((StackPanel)root.FindName("VideoAudioPanel")).Visibility = Visibility.Visible;
            Check(((MediaPlayerElement)root.FindName("ViewerVideo")).Stretch == Microsoft.UI.Xaml.Media.Stretch.Uniform,
                "视频应在现有窗口内等比完整显示");
            Check(Grid.GetColumn((ToggleButton)root.FindName("DetailsToggle")) == 2
                && Grid.GetColumn((ToggleButton)root.FindName("PlaylistToggle")) == 3
                && Grid.GetColumn((Button)root.FindName("ViewerFullScreenButton")) == 4, "详细信息和播放列表应并排放在全屏按钮左边");
            Set("_playlistOpen", false);
            typeof(MainWindow).GetMethod("ApplyPlaylistLayout", flags)!.Invoke(window, null);
            typeof(MainWindow).GetMethod("SetDetailsOpen", flags)!.Invoke(window, [false]);
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 650));
            await Task.Delay(50);
            root.UpdateLayout();
            var panelStage = (Grid)root.FindName("ViewerStage");
            double stageWidth = panelStage.ActualWidth, stageHeight = panelStage.ActualHeight;
            var originalSize = window.AppWindow.Size;
            var stableControls = new[] { "VideoControls", "ViewerBar", "VideoSeek", "DetailsToggle", "PlaylistToggle", "ViewerFullScreenButton" }
                .Select(name => (FrameworkElement)root.FindName(name)).ToArray();
            Windows.Foundation.Rect ControlBounds(FrameworkElement element) => element.TransformToVisual(root)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            var originalControlBounds = stableControls.Select(ControlBounds).ToArray();
            void CheckStableControls(string action)
            {
                Check(stableControls.Select(ControlBounds).Zip(originalControlBounds).All(pair =>
                    Math.Abs(pair.First.X - pair.Second.X) < 1 && Math.Abs(pair.First.Y - pair.Second.Y) < 1
                    && Math.Abs(pair.First.Width - pair.Second.Width) < 1 && Math.Abs(pair.First.Height - pair.Second.Height) < 1),
                    $"{action}不能改变控制栏宽度、进度条或底栏按钮位置");
            }
            typeof(MainWindow).GetMethod("SetPlaylistOpen", flags)!.Invoke(window, [true]);
            await Task.Delay(50);
            root.UpdateLayout();
            var expandedSize = window.AppWindow.Size;
            CheckStableControls("展开播放列表");
            var dockedPanel = (Grid)root.FindName("ViewerSidePanel");
            Check(Math.Abs(ControlBounds(dockedPanel).Bottom - ControlBounds(stableControls[1]).Bottom) < 1,
                "停靠面板应延伸至独立侧栏底部");
            Check(Math.Abs(panelStage.ActualWidth - stageWidth) < 1 && Math.Abs(panelStage.ActualHeight - stageHeight) < 1,
                "展开播放列表不能改变画面尺寸");
            Check(expandedSize.Width > originalSize.Width && expandedSize.Height == originalSize.Height,
                "普通窗口应只向右扩展宽度");
            typeof(MainWindow).GetMethod("SetDetailsOpen", flags)!.Invoke(window, [true]);
            root.UpdateLayout();
            Check(((Grid)root.FindName("DetailsPanel")).Visibility == Visibility.Visible
                && ((Grid)root.FindName("PlaylistPanel")).Visibility == Visibility.Collapsed
                && ((ToggleButton)root.FindName("DetailsToggle")).IsChecked == true
                && ((ToggleButton)root.FindName("PlaylistToggle")).IsChecked == false,
                "切换详细信息应关闭播放列表并同步按钮");
            Check(window.AppWindow.Size.Width == expandedSize.Width && Math.Abs(panelStage.ActualWidth - stageWidth) < 1,
                "面板内容切换不能再次扩展窗口或改变画面");
            CheckStableControls("切换详细信息");
            typeof(MainWindow).GetMethod("SetDetailsOpen", flags)!.Invoke(window, [false]);
            await Task.Delay(50);
            root.UpdateLayout();
            Check(window.AppWindow.Size.Width == originalSize.Width && window.AppWindow.Size.Height == originalSize.Height
                && Math.Abs(panelStage.ActualWidth - stageWidth) < 1, "收起面板应恢复窗口宽度，画面不变");
            CheckStableControls("收起详细信息");
            Set("_dockViewerPanel", false);
            Set("_playlistOpen", true);
            typeof(MainWindow).GetMethod("ApplyPlaylistLayout", flags)!.Invoke(window, null);
            root.UpdateLayout();
            Check(Math.Abs(panelStage.ActualWidth - stageWidth) < 1 && ((Grid)root.FindName("ViewerSidePanel")).Visibility == Visibility.Visible,
                "浮层面板不能占据画面布局空间");
            var overlayPanel = (Grid)root.FindName("ViewerSidePanel");
            var panelBounds = overlayPanel.TransformToVisual(panelStage).TransformBounds(new Windows.Foundation.Rect(0, 0, overlayPanel.ActualWidth, overlayPanel.ActualHeight));
            Check(Math.Abs(panelBounds.Right - panelStage.ActualWidth) < 1 && panelBounds.Left < panelStage.ActualWidth,
                "浮层应靠画面右侧向内展开");
            CheckStableControls("浮层展开");
            Check(ControlBounds(overlayPanel).Bottom <= ControlBounds(stableControls[0]).Top + 1,
                "浮层不能遮挡进度条和底部面板开关");
            typeof(MainWindow).GetMethod("SetPlaylistOpen", flags)!.Invoke(window, [false]);
            Set("_dockViewerPanel", true);
            Set("_presenterBeforeFullScreen", window.AppWindow.Presenter);
            typeof(MainWindow).GetMethod("ResetFullScreenControls", flags)!.Invoke(window, null);
            root.UpdateLayout();
            var bottomControls = (Grid)root.FindName("ViewerBar");
            var seekControls = (Grid)root.FindName("VideoControls");
            var fullScreenStageSize = new Windows.Foundation.Size(panelStage.ActualWidth, panelStage.ActualHeight);
            Check(bottomControls.Opacity == 0 && seekControls.Opacity == 0 && !bottomControls.IsHitTestVisible
                && ((Border)root.FindName("FullScreenControlsHotspot")).Visibility == Visibility.Visible,
                "全屏默认隐藏控制栏，并保留底部鼠标唤起区域");
            Check(Math.Abs(panelStage.ActualHeight - ((Grid)root.FindName("Viewer")).ActualHeight) < 1,
                "隐藏控制栏后画面应占满全屏高度，不留底部空条");
            typeof(MainWindow).GetMethod("ShowFullScreenControls", flags)!.Invoke(window, null);
            root.UpdateLayout();
            Check(bottomControls.Opacity == 1 && seekControls.IsHitTestVisible
                && Math.Abs(panelStage.ActualHeight - fullScreenStageSize.Height) < 1
                && Math.Abs(panelStage.ActualWidth - fullScreenStageSize.Width) < 1,
                "唤起全屏控制栏不能改变画面尺寸");
            Set("_fullScreenControlPressed", true);
            typeof(MainWindow).GetMethod("HideFullScreenControlsIfIdle", flags)!.Invoke(window, null);
            Check(bottomControls.Opacity == 1, "拖动全屏控制栏滑块时不能隐藏");
            Set("_fullScreenControlPressed", false);
            Set("_pointerOverFullScreenControls", true);
            typeof(MainWindow).GetMethod("HideFullScreenControlsIfIdle", flags)!.Invoke(window, null);
            Check(bottomControls.Opacity == 1, "鼠标停在全屏控制栏时保持显示");
            Set("_pointerOverFullScreenControls", false);
            typeof(MainWindow).GetMethod("ScheduleFullScreenControlsHide", flags)!.Invoke(window, null);
            await Task.Delay(900);
            root.UpdateLayout();
            Check(bottomControls.Opacity == 0 && Math.Abs(panelStage.ActualHeight - fullScreenStageSize.Height) < 1,
                "离开控制区后定时隐藏且画面大小不变");
            Set("_presenterBeforeFullScreen", null!);
            typeof(MainWindow).GetMethod("ResetFullScreenControls", flags)!.Invoke(window, null);
            root.UpdateLayout();
            Check(bottomControls.Opacity == 1 && bottomControls.IsHitTestVisible && Grid.GetRowSpan(panelStage) == 1
                && ((Border)root.FindName("FullScreenControlsHotspot")).Visibility == Visibility.Collapsed,
                "退出全屏应恢复常驻底栏及普通画面布局");
            typeof(MainWindow).GetMethod("UpdateViewerOverlays", flags)!.Invoke(window, null);
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 720));
            window.ViewerState.SetPlaylist(items.AsEnumerable().Reverse().ToList());
            Check(ReferenceEquals(window.ViewerState.CurrentItem, items[0]) && window.ViewerState.CurrentIndex == 1,
                "重排播放列表必须保持当前媒体");
            Check(((TextBlock)root.FindName("ViewerCounter")).Text == "(2 / 2)" && !((Button)root.FindName("NextButton")).IsEnabled,
                "重排后查看器计数或切换按钮绑定未更新");
            var nextRequest = window.ViewerState.BeginOpen(0)!;
            Check(request.Token.IsCancellationRequested && !nextRequest.Token.IsCancellationRequested, "查看器切换必须取消旧请求");
            window.ViewerState.Close();
            Check(nextRequest.Token.IsCancellationRequested && !window.ViewerState.IsOpen && window.ViewerState.Message == "", "关闭应清理查看器状态并取消请求");
            Check(((Grid)root.FindName("Viewer")).Visibility == Visibility.Collapsed
                && ((TextBlock)root.FindName("ViewerMessage")).Visibility == Visibility.Collapsed, "关闭后覆盖层或加载提示仍然显示");
            root.UpdateLayout();
            Check(((TitleBar)root.FindName("AppTitleBar")).ActualTheme == ElementTheme.Light
                && ((Grid)root.FindName("GalleryToolbar")).Visibility == Visibility.Visible,
                "关闭深色查看器后必须恢复亮色导航栏");
            Check(PassesThrough((Button)root.FindName("SettingsButton"))
                && PassesThrough((Button)root.FindName("UpButton")), "返回浏览后导航及设置应恢复可点击区域");
            typeof(MainWindow).GetMethod("ApplyTheme", flags)!.Invoke(window, new object[] { 2 });
            window.ViewerState.SetPlaylist(window.GalleryState.MediaItems);
            Check(items.All(i => i.Version is { } key && MediaCache.AspectRatios.TryGet(key, out _)),
                "目录探测没有把宽高比保留在内存");
            bool ratiosReadyAtCommit = false;
            window.GalleryState.FolderCommitted += (_, _) => ratiosReadyAtCommit = window.GalleryState.MediaItems.All(i => i.HasAspectRatio);
            await Invoke("NavigateAsync", folder, false, true);
            Check(ratiosReadyAtCommit, "内存宽高比应在首次布局前恢复");
            await Invoke("NavigateAsync", Path.Combine(workspace, "work", "validation", "experience", "10-混合"), true, false);
            await Invoke("SetFilterAsync", MediaFilter.Photos, true);
            Check(window.GalleryState.MediaItems.Count > 0 && window.GalleryState.MediaItems.All(item => !item.IsVideo)
                && window.ViewerState.Playlist.All(item => !item.IsVideo), "图片筛选必须同步卡片和查看器播放列表");
            await Invoke("ShowInViewerAsync", 0, false);
            await Invoke("SetFilterAsync", MediaFilter.Videos, false);
            Check(!window.ViewerState.IsOpen && window.GalleryState.Items.Count > 0
                && window.GalleryState.Items.All(item => item.IsVideo), "仅视频且隐藏文件夹时应关闭旧图片并只显示视频");
            Check(((RadioMenuFlyoutItem)root.FindName("FilterVideosItem")).IsChecked
                && ((RadioMenuFlyoutItem)root.FindName("HideSubfoldersItem")).IsChecked,
                "筛选菜单的选中状态必须同步");
            var filterSettings = (AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!;
            Check(filterSettings.Filter == MediaFilter.Videos && !filterSettings.ShowSubfolders, "保存入口必须包含筛选状态");
            string? previousFilterFolder = window.GalleryState.PreviousFolder;
            var oldFilterRequest = Invoke("SetFilterAsync", MediaFilter.Photos, true);
            var newFilterRequest = Invoke("SetFilterAsync", MediaFilter.Videos, false);
            await Task.WhenAll(oldFilterRequest, newFilterRequest);
            Check(window.GalleryState.Items.Count > 0 && window.GalleryState.Items.All(item => item.IsVideo)
                && window.GalleryState.PreviousFolder == previousFilterFolder && !window.GalleryState.IsScanning,
                "快速切换筛选必须以最后选择为准且不能增加历史记录");
            await Invoke("SetFilterAsync", MediaFilter.All, true);
            await Invoke("NavigateAsync", folder, true, false);
            window.GalleryState.SortField = MediaSortField.Created;
            window.GalleryState.Descending = true;
            await Invoke("ApplySortAsync");
            typeof(MainWindow).GetMethod("UpdateBrowseMenus", flags)!.Invoke(window, null);
            Check(((RadioMenuFlyoutItem)root.FindName("SortCreatedItem")).IsChecked
                && ((RadioMenuFlyoutItem)root.FindName("SortDescendingItem")).IsChecked
                && !((RadioMenuFlyoutItem)root.FindName("SortAscendingItem")).IsChecked, "排序字段和方向必须分别单选");
            await Invoke("SetFilterAsync", MediaFilter.Videos, true);
            Check(window.GalleryState.Items.Count == 0 && ((TextBlock)root.FindName("EmptyText")).Text.Contains("筛选"),
                "筛选无结果时应说明筛选条件，而非误报目录为空");
            await Invoke("SetFilterAsync", MediaFilter.All, true);
            var settingsDialog = (ContentDialog)typeof(MainWindow).GetMethod("BuildSettingsDialog", flags)!.Invoke(window, null)!;
            Check(settingsDialog.PrimaryButtonText == "完成" && settingsDialog.CloseButtonText == "取消"
                && ((StackPanel)((ScrollViewer)settingsDialog.Content).Content).Children.OfType<ComboBox>().Count() == 2,
                "设置面板应提供可保存或取消的主题和视图选项");
            await Invoke("ApplyPreferencesAsync", 1, GalleryDisplayMode.LargeIcons, false, 280d);
            Check(window.GalleryState.DisplayMode == GalleryDisplayMode.LargeIcons && !window.GalleryState.ShowFileNames
                && (int)Get("_themeIndex")! == 1 && ((Slider)root.FindName("ThumbSize")).Value == 280,
                "设置保存必须应用到现有控件和视图");
            await Invoke("ApplyPreferencesAsync", 2, GalleryDisplayMode.Masonry, true, 320d);
            window.GalleryState.SortField = MediaSortField.Name;
            window.GalleryState.Descending = false;
            await Invoke("ApplySortAsync");
            using (var gallery = new GalleryViewModel())
            {
                var oldNavigation = gallery.NavigateAsync(Path.Combine(workspace, "work", "validation", "large"));
                var newNavigation = gallery.NavigateAsync(folder);
                await Task.WhenAll(oldNavigation, newNavigation);
                Check(gallery.CurrentFolder == folder && gallery.MediaItems.Count == 2 && !gallery.IsScanning && !gallery.CanGoBack,
                    "旧扫描不能覆盖最新目录或污染历史");
                Check(!await gallery.NavigateAsync(Path.Combine(folder, "does-not-exist")) && gallery.CurrentFolder == folder && gallery.Items.Count == 2,
                    "失败导航不能清空已成功的目录");
                var other = Path.Combine(workspace, "work", "validation", "experience", "10-混合");
                await gallery.NavigateAsync(other);
                Check(gallery.PreviousFolder == folder && gallery.CanGoBack, "导航历史应由 GalleryViewModel 维护");
                await gallery.NavigateAsync(folder, pushHistory: false);
                Check(!gallery.CanGoBack && gallery.CurrentFolder == folder, "后退应正确消费历史");
                Check(gallery.CanGoForward && gallery.NextFolder == other, "后退后应保留前进目录");
                await gallery.NavigateAsync(Path.Combine(folder, "missing"));
                Check(gallery.CanGoForward && gallery.NextFolder == other, "失败的新导航不能清除前进历史");
                await gallery.NavigateAsync(other, pushHistory: false, forward: true);
                Check(gallery.CurrentFolder == other && gallery.PreviousFolder == folder && !gallery.CanGoForward, "前进应只移动历史游标");
                await gallery.NavigateAsync(folder, pushHistory: false);
                await gallery.NavigateAsync(Path.Combine(workspace, "work", "validation", "experience", "02-空目录"));
                Check(!gallery.CanGoForward, "后退后打开新目录应清除旧前进分支");
                await gallery.NavigateAsync(folder, pushHistory: false);
                gallery.SortField = MediaSortField.Size;
                var oldSort = gallery.ApplySortAsync();
                gallery.SortField = MediaSortField.Name;
                gallery.Descending = true;
                var newSort = gallery.ApplySortAsync();
                await Task.WhenAll(oldSort, newSort);
                Check(gallery.Items[0].Name == "image2.jpg", "旧排序结果不能覆盖最新选项");
                gallery.SortField = MediaSortField.Taken;
                var oldDateSort = gallery.ApplySortAsync();
                gallery.SortField = MediaSortField.Name;
                gallery.Descending = false;
                var latestSort = gallery.ApplySortAsync();
                await Task.WhenAll(oldDateSort, latestSort);
                Check(gallery.Items[0].Name == "image1.jpg", "取消的拍摄日期读取不能覆盖后续名称排序");
            }
            using (var gallery = new GalleryViewModel())
            {
                var fixture = Path.Combine(workspace, "work", "validation", "experience-checks", Guid.NewGuid().ToString("N"));
                var firstFolder = Directory.CreateDirectory(Path.Combine(fixture, "first")).FullName;
                var secondFolder = Directory.CreateDirectory(Path.Combine(fixture, "second")).FullName;
                var photo = Path.Combine(firstFolder, "photo.jpg");
                var source = Path.Combine(workspace, "work", "validation", "media", "image1.jpg");
                File.Copy(source, photo);
                await gallery.NavigateAsync(firstFolder);
                var firstItem = gallery.Items.Single();
                await firstItem.EnsureThumbnailAsync(128);
                var thumbnail = firstItem.Thumbnail;
                await gallery.NavigateAsync(secondFolder);
                Check(firstItem.Thumbnail is null, "离开目录后模型不能绕过图片缓存预算保留解码图");
                await gallery.NavigateAsync(firstFolder);
                Check(ReferenceEquals(firstItem, gallery.Items.Single()) && firstItem.HasAspectRatio,
                    "返回目录应复用未变化的媒体对象及尺寸");
                var warmLoad = firstItem.EnsureThumbnailAsync(128);
                Check(warmLoad.IsCompletedSuccessfully && thumbnail is not null && ReferenceEquals(firstItem.Thumbnail, thumbnail),
                    "返回目录应同步复用已解码图片");
                await gallery.NavigateAsync(secondFolder);
                File.Copy(Path.Combine(workspace, "work", "validation", "media", "image2.jpg"), photo, true);
                File.SetLastWriteTimeUtc(photo, DateTime.UtcNow.AddSeconds(10));
                File.Copy(source, Path.Combine(firstFolder, "added.jpg"));
                await gallery.NavigateAsync(firstFolder);
                var replaced = gallery.Items.Single(i => i.Name == "photo.jpg");
                Check(gallery.Items.Count == 2 && !ReferenceEquals(firstItem, replaced) && replaced.Version != firstItem.Version,
                    "重新扫描必须发现新增和覆盖，不复用旧版本模型");
                File.Delete(photo);
                await gallery.NavigateAsync(firstFolder, refresh: true);
                Check(gallery.Items.Count == 1 && gallery.Items[0].Name == "added.jpg", "刷新后不能残留已删除文件");
                var cached = gallery.Items.Single();
                for (int i = 0; i < 40; i++)
                    await gallery.NavigateAsync(Directory.CreateDirectory(Path.Combine(fixture, $"empty-{i}")).FullName);
                await gallery.NavigateAsync(firstFolder);
                Check(!ReferenceEquals(cached, gallery.Items.Single()), "长时间浏览多个目录后旧目录模型应被淘汰");
                cached = gallery.Items.Single();
                gallery.ClearCachedDirectories();
                await gallery.NavigateAsync(firstFolder, refresh: true);
                Check(!ReferenceEquals(cached, gallery.Items.Single()), "清除缓存后不能继续复用目录快照");
            }
            typeof(MainWindow).GetMethod("SettingsTimer_Tick", flags)!.Invoke(window, new object?[] { null, EventArgs.Empty });
            AppSettings saved = new();
            for (int i = 0; i < 100; i++)
            {
                saved = await store.LoadAsync();
                // 同一路径可能已由防抖计时器保存过，需等到整组新设置落盘。
                if (saved.LastFolder == folder && saved.Theme == 2 && saved.SortField == MediaSortField.Name && !saved.Descending) break;
                await Task.Delay(20);
            }
            Check(saved.LastFolder == folder && saved.Theme == 2 && saved.SortField == MediaSortField.Name && !saved.Descending,
                "窗口保存接线没有落盘最新目录、主题和排序");
            var itemsBeforeClear = window.GalleryState.Items;
            await Invoke("ClearCacheAsync");
            Check(ReferenceEquals(itemsBeforeClear, window.GalleryState.Items)
                && ReferenceEquals(((ItemsRepeater)root.FindName("Gallery")).ItemsSource, itemsBeforeClear),
                "清除缓存后必须重新绑定现有卡片而非留下空白");
            Check(MediaCache.AspectRatios.Count == 0, "清除入口没有清理内存宽高比");
            typeof(MainWindow).GetMethod("CloseViewer", flags)!.Invoke(window, null);
            typeof(MainWindow).GetMethod("SetPlaylistOpen", flags)!.Invoke(window, [false]);
            typeof(MainWindow).GetMethod("SetDetailsOpen", flags)!.Invoke(window, [false]);
            for (int round = 0; round < 2; round++)
            {
                window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(80 + round * 20, 90, 900, 650));
                await Task.Delay(50);
                var browsePosition = window.AppWindow.Position;
                var browseSize = window.AppWindow.Size;
                await Invoke("ShowInViewerAsync", 0, false);
                typeof(MainWindow).GetMethod("SetPlaylistOpen", flags)!.Invoke(window, [true]);
                await Invoke("ShowInViewerAsync", 1, false);
                window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(180, 150, 1000, 700));
                var duringPlayback = (AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!;
                Check(duringPlayback.Width == browseSize.Width && duringPlayback.Height == browseSize.Height,
                    "播放期间保存设置不能覆盖浏览窗口尺寸");
                typeof(MainWindow).GetMethod("CloseViewer", flags)!.Invoke(window, null);
                await Task.Delay(50);
                Check(window.AppWindow.Position.X == browsePosition.X && window.AppWindow.Position.Y == browsePosition.Y
                    && window.AppWindow.Size.Width == browseSize.Width && window.AppWindow.Size.Height == browseSize.Height,
                    "返回浏览应恢复进入前的位置和大小，切换媒体及再次打开不能覆盖快照");
            }
            var treeFolder = Path.GetDirectoryName(folder)!;
            var invokedNode = new TreeViewNode { Content = new FolderEntry(treeFolder, "目录展开检查"), HasUnrealizedChildren = true };
            tree.RootNodes.Add(invokedNode);
            await Invoke("NavigateTreeNodeAsync", invokedNode);
            Check(window.GalleryState.CurrentFolder == treeFolder && invokedNode.IsExpanded && invokedNode.Children.Count > 0,
                "点击文件树目录应导航并加载展开其子目录");
            invokedNode.IsExpanded = false;
            var sameFolderItems = window.GalleryState.Items;
            await Invoke("NavigateTreeNodeAsync", invokedNode);
            Check(invokedNode.IsExpanded && ReferenceEquals(sameFolderItems, window.GalleryState.Items),
                "再次点击当前目录应展开，但不重复扫描媒体");
            var leafNode = invokedNode.Children.First(n => ((FolderEntry)n.Content).Path == folder);
            await Invoke("NavigateTreeNodeAsync", leafNode);
            Check(window.GalleryState.CurrentFolder == folder && leafNode.Children.Count == 0 && !leafNode.HasUnrealizedChildren,
                "无子目录的叶节点应正常导航且结束懒加载");
            string watchRoot = Directory.CreateDirectory(Path.Combine(workspace, "work", "validation", "watch-" + Guid.NewGuid().ToString("N"))).FullName;
            string album = Directory.CreateDirectory(Path.Combine(watchRoot, "album")).FullName;
            string mediaSource = Path.Combine(workspace, "work", "validation", "media", "image2.jpg");
            File.Copy(mediaSource, Path.Combine(watchRoot, "keep.jpg"));
            File.Copy(mediaSource, Path.Combine(album, "photo.jpg"));
            await Invoke("NavigateAsync", watchRoot, true, false);
            await Invoke("SelectCurrentFolderAsync", watchRoot, CancellationToken.None);
            var watchedNode = (TreeViewNode)Get("_currentFolderNode")!;
            typeof(MainWindow).GetMethod("InitializeFolderMonitoring", flags)!.Invoke(window, null);
            await Invoke("WatchFolderChangesAsync", watchRoot);
            async Task WaitForChange(Func<bool> condition)
            {
                for (int attempt = 0; attempt < 160 && !condition(); attempt++) await Task.Delay(50);
                Check(condition(), "目录变化未在超时前自动同步");
            }
            await Invoke("ShowInViewerAsync", 0, false);
            var retainedImage = ((Image)root.FindName("ViewerImage")).Source;
            File.Delete(Path.Combine(album, "photo.jpg"));
            Directory.Delete(album);
            await WaitForChange(() => !window.GalleryState.IsScanning && window.GalleryState.Items.All(item => item.Path != album)
                && watchedNode.Children.All(node => ((FolderEntry)node.Content).Path != album));
            Check(window.ViewerState.IsOpen && ReferenceEquals(retainedImage, ((Image)root.FindName("ViewerImage")).Source),
                "删除其他目录的自动刷新不能关闭或重新加载正在查看的文件");
            typeof(MainWindow).GetMethod("CloseViewer", flags)!.Invoke(window, null);
            string added = Directory.CreateDirectory(Path.Combine(watchRoot, "added")).FullName;
            File.Copy(mediaSource, Path.Combine(added, "photo.jpg"));
            await WaitForChange(() => window.GalleryState.Items.Any(item => item.Path == added)
                && watchedNode.Children.Any(node => ((FolderEntry)node.Content).Path == added));
            string renamed = Path.Combine(watchRoot, "renamed");
            // 系统缩略图可能短暂持有新目录中的文件；等预览读取释放后模拟外部改名。
            for (int attempt = 0; ; attempt++)
            {
                try { Directory.Move(added, renamed); break; }
                catch (IOException) when (attempt < 20) { await Task.Delay(100); }
            }
            await WaitForChange(() => window.GalleryState.Items.All(item => item.Path != added)
                && window.GalleryState.Items.Any(item => item.Path == renamed)
                && watchedNode.Children.Any(node => ((FolderEntry)node.Content).Path == renamed));
            await Invoke("NavigateAsync", renamed, true, false);
            await Invoke("WatchFolderChangesAsync", renamed);
            File.Delete(Path.Combine(renamed, "photo.jpg"));
            Directory.Delete(renamed);
            await WaitForChange(() => window.GalleryState.CurrentFolder == watchRoot && !window.GalleryState.IsScanning);
            await Invoke("NavigateAsync", folder, true, false);
            await Invoke("WatchFolderChangesAsync", folder);
            File.Delete(Path.Combine(watchRoot, "keep.jpg"));
            await Task.Delay(650);
            Check(window.GalleryState.CurrentFolder == folder && window.GalleryState.MediaItems.Count == 2,
                "离开目录后的旧监听事件不能覆盖当前列表");
            string firstScreenFolder = Directory.CreateDirectory(Path.Combine(workspace, "work", "validation", "first-screen-" + Guid.NewGuid().ToString("N"))).FullName;
            for (int index = 0; index < 201; index++) File.Copy(mediaSource, Path.Combine(firstScreenFolder, $"photo-{index:000}.jpg"));
            using (var coldGallery = new GalleryViewModel { DisplayMode = GalleryDisplayMode.Justified })
            {
                bool firstBatchReady = false, tailDeferred = false;
                coldGallery.ItemsChanged += () =>
                {
                    firstBatchReady = coldGallery.MediaItems.Take(200).All(item => item.HasAspectRatio && Math.Abs(item.AspectRatio - 1) > 0.01);
                    tailDeferred = !coldGallery.MediaItems[200].HasAspectRatio;
                };
                await coldGallery.NavigateAsync(firstScreenFolder);
                Check(firstBatchReady, "自适应首批列表发布时必须已有真实宽高比，不能先显示方形占位");
                Check(tailDeferred && coldGallery.MediaItems.All(item => item.HasAspectRatio), "首批只等待 200 项，剩余宽高比应继续后台完成");
            }
            string sideButtonFolder = Path.Combine(workspace, "work", "validation", "experience", "02-空目录");
            await Invoke("NavigateAsync", sideButtonFolder, true, false);
            await Invoke("HandleMouseNavigationAsync", false);
            Check(window.GalleryState.CurrentFolder == folder && window.GalleryState.CanGoForward,
                "鼠标后退应使用目录历史并保留前进入口");
            Set("_settingsDialog", new ContentDialog());
            await Invoke("HandleMouseNavigationAsync", true);
            Check(window.GalleryState.CurrentFolder == folder, "设置对话框中鼠标侧键不得导航");
            Set("_settingsDialog", null!);
            typeof(MainWindow).GetMethod("BeginAddressEdit", flags)!.Invoke(window, null);
            await Invoke("HandleMouseNavigationAsync", true);
            Check(window.GalleryState.CurrentFolder == folder, "地址编辑中鼠标侧键不得导航");
            typeof(MainWindow).GetMethod("EndAddressEdit", flags)!.Invoke(window, [false]);
            await Invoke("HandleMouseNavigationAsync", true);
            Check(window.GalleryState.CurrentFolder == sideButtonFolder && !window.GalleryState.CanGoForward,
                "鼠标前进应与左上角前进按钮一致");
            await Invoke("HandleMouseNavigationAsync", true);
            Check(window.GalleryState.CurrentFolder == sideButtonFolder, "没有前进历史时侧键不应跳转");
            await Invoke("HandleMouseNavigationAsync", false);
            await Invoke("ShowInViewerAsync", 0, false);
            var sideButtonImage = window.ViewerState.CurrentItem;
            await Invoke("HandleMouseNavigationAsync", true);
            await Invoke("HandleMouseNavigationAsync", false);
            Check(window.GalleryState.CurrentFolder == folder && ReferenceEquals(window.ViewerState.CurrentItem, sideButtonImage),
                "图片查看时鼠标侧键不得切换图片或目录");
            var rotationPanel = (Fallery.Controls.QuarterTurnPanel)root.FindName("ImageRotation");
            var rotatingImage = (Image)root.FindName("ViewerImage");
            var originalImageSource = rotatingImage.Source;
            foreach (int degrees in new[] { 90, 180, 270, 0 })
            {
                typeof(MainWindow).GetMethod("RotateMedia_Click", flags)!.Invoke(window, [root, new RoutedEventArgs()]);
                root.UpdateLayout();
                var rotatedBounds = rotatingImage.TransformToVisual(rotationPanel).TransformBounds(
                    new Windows.Foundation.Rect(0, 0, rotatingImage.ActualWidth, rotatingImage.ActualHeight));
                Check(rotationPanel.QuarterTurns * 90 == degrees && ReferenceEquals(rotatingImage.Source, originalImageSource)
                    && Math.Abs(rotatedBounds.X) < 1 && Math.Abs(rotatedBounds.Y) < 1
                    && Math.Abs(rotatedBounds.Width - rotationPanel.ActualWidth) < 1
                    && Math.Abs(rotatedBounds.Height - rotationPanel.ActualHeight) < 1,
                    $"图片旋转 {degrees}° 应保留图源并使用旋转后的完整布局边界");
            }
            typeof(MainWindow).GetMethod("RotateViewerMedia", flags)!.Invoke(window, null);
            await Invoke("ShowInViewerAsync", 1, false);
            Check(rotationPanel.QuarterTurns == 0 && (int)Get("_viewerQuarterTurns")! == 0,
                "切换图片必须恢复 0°，不能沿用上一张的方向");
            window.ViewerState.SetPlaylist([new MediaItem(Path.Combine(workspace, "work", "validation", "media", "motion.gif"), MediaKind.Image)]);
            await Invoke("ShowInViewerAsync", 0, false);
            var gifSource = (Microsoft.UI.Xaml.Media.Imaging.BitmapImage)rotatingImage.Source;
            typeof(MainWindow).GetMethod("RotateViewerMedia", flags)!.Invoke(window, null);
            Check(ReferenceEquals(rotatingImage.Source, gifSource) && gifSource.AutoPlay && rotationPanel.QuarterTurns == 1,
                "旋转 GIF 不能替换图源或关闭动画");
            typeof(MainWindow).GetMethod("CloseViewer", flags)!.Invoke(window, null);
            var seekPlayer = new MouseSeekPlayer();
            var sideButtonVideo = new MediaItem(Path.Combine(workspace, "work", "validation", "media", "video.mp4"), MediaKind.Video);
            window.ViewerState.SetPlaylist([sideButtonVideo]);
            window.ViewerState.BeginOpen(0);
            Set("_videoPlayer", seekPlayer);
            var actualVideo = (MediaPlayerElement)root.FindName("ViewerVideo");
            actualVideo.Opacity = 0;
            await Invoke("HandleMouseNavigationAsync", true);
            Check(seekPlayer.Position == TimeSpan.FromSeconds(20), "视频首帧准备期间侧键不应跳转");
            actualVideo.Opacity = 1;
            await Invoke("HandleMouseNavigationAsync", true);
            Check(seekPlayer.Position == TimeSpan.FromSeconds(25) && !seekPlayer.IsPlaying,
                "视频鼠标前进应快进 5 秒且保持暂停状态");
            seekPlayer.Play();
            await Invoke("HandleMouseNavigationAsync", false);
            Check(seekPlayer.Position == TimeSpan.FromSeconds(20) && seekPlayer.IsPlaying
                && window.GalleryState.CurrentFolder == folder && ReferenceEquals(window.ViewerState.CurrentItem, sideButtonVideo),
                "视频鼠标后退应快退 5 秒，保持播放和当前媒体，不触发目录历史");
            typeof(MainWindow).GetMethod("CloseViewer", flags)!.Invoke(window, null);
            window.ViewerState.SetPlaylist(window.GalleryState.MediaItems);
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(90, 100, 900, 650));
            await Task.Delay(50);
            var videoBrowseSize = window.AppWindow.Size;
            var videoBrowsePosition = window.AppWindow.Position;
            window.ViewerState.SetPlaylist(new[] { 16d / 9, 9d / 16, 21d / 9 }.Select((ratio, index) =>
                new MediaItem(Path.Combine(workspace, "work", "validation", "media", index == 1 ? "video.mov" : "video.mp4"), MediaKind.Video)
                { HasAspectRatio = true, AspectRatio = ratio }).ToList());
            for (int index = 0; index < 3; index++)
            {
                await Invoke("ShowInViewerAsync", index, false);
                var nativePlayer = (Fallery.Services.Playback.IPlayer)Get("_videoPlayer")!;
                for (int attempt = 0; attempt < 100 && nativePlayer.VideoWidth == 0; attempt++) await Task.Delay(30);
                await Task.Delay(100);
                root.UpdateLayout();
                Check(nativePlayer.VideoWidth > 0 && window.AppWindow.Size.Width == videoBrowseSize.Width
                    && window.AppWindow.Size.Height == videoBrowseSize.Height
                    && window.AppWindow.Position.X == videoBrowsePosition.X && window.AppWindow.Position.Y == videoBrowsePosition.Y,
                    $"打开或切换视频 {index}，缓存比例和实际尺寸就绪后都不能改变浏览窗口位置/大小");
                if (index == 0)
                {
                    nativePlayer.Pause();
                    for (int attempt = 0; attempt < 100 && nativePlayer.IsPlaying; attempt++) await Task.Delay(30);
                    uint videoWidth = nativePlayer.VideoWidth, videoHeight = nativePlayer.VideoHeight;
                    var positionBeforeRotation = nativePlayer.Position;
                    foreach (int degrees in new[] { 90, 180, 270, 0 })
                    {
                        typeof(MainWindow).GetMethod("RotateViewerMedia", flags)!.Invoke(window, null);
                        root.UpdateLayout();
                        bool swapDimensions = degrees is 90 or 270;
                        Check(nativePlayer.Rotation == degrees
                            && nativePlayer.VideoWidth == (swapDimensions ? videoHeight : videoWidth)
                            && nativePlayer.VideoHeight == (swapDimensions ? videoWidth : videoHeight)
                            && nativePlayer.Position == positionBeforeRotation && !nativePlayer.IsPlaying,
                            $"原生视频旋转 {degrees}° 应保持暂停位置，并报告旋转后的显示尺寸；实际 {nativePlayer.Rotation}°，{nativePlayer.VideoWidth}×{nativePlayer.VideoHeight}（原 {videoWidth}×{videoHeight}），位置 {nativePlayer.Position}/{positionBeforeRotation}，播放 {nativePlayer.IsPlaying}");
                        var surface = (Fallery.Controls.AspectFitPanel)root.FindName("VideoSurface");
                        var bounds = actualVideo.TransformToVisual(surface).TransformBounds(
                            new Windows.Foundation.Rect(0, 0, actualVideo.ActualWidth, actualVideo.ActualHeight));
                        double ratio = (double)nativePlayer.VideoWidth / nativePlayer.VideoHeight;
                        Check(bounds.Width > 0 && bounds.Height > 0
                            && Math.Abs(bounds.Width - bounds.Height * ratio) < 2
                            && Math.Abs(bounds.X * 2 + bounds.Width - surface.ActualWidth) < 2
                            && Math.Abs(bounds.Y * 2 + bounds.Height - surface.ActualHeight) < 2
                            && bounds.Width <= surface.ActualWidth + 1 && bounds.Height <= surface.ActualHeight + 1,
                            $"视频 {degrees}° 的原生表面必须按比例居中，留白位于播放器外部；实际 {bounds}，区域 {surface.ActualWidth}×{surface.ActualHeight}");
                    }
                    typeof(MainWindow).GetMethod("RotateViewerMedia", flags)!.Invoke(window, null);
                }
                else Check(nativePlayer.Rotation == 0 && (int)Get("_viewerQuarterTurns")! == 0,
                    "切换视频后播放器和窗口方向均应恢复 0°");
            }
            typeof(MainWindow).GetMethod("CloseViewer", flags)!.Invoke(window, null);
            await Task.Delay(50);
            Check(window.AppWindow.Size.Width == videoBrowseSize.Width && window.AppWindow.Size.Height == videoBrowseSize.Height
                && window.AppWindow.Position.X == videoBrowsePosition.X && window.AppWindow.Position.Y == videoBrowsePosition.Y,
                "视频返回浏览时窗口位置和大小应保持不变");
            window.ViewerState.SetPlaylist(window.GalleryState.MediaItems);
            Check(!actualVideo.AutoPlay, "视频不能在窗口和播放位置准备前自动播放");
            var beforeDefaults = (AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!;
            await store.SaveAsync(beforeDefaults with { Width = 1100, Height = 720, Maximized = true, VideoWindowDefaultsApplied = false });
            await Invoke("RestoreSettingsAsync");
            Check(((AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!).VideoWindowDefaultsApplied,
                "旧窗口设置应迁移一次为默认 16:9 画面尺寸");
            await Task.Delay(50);
            window.ViewerState.SetPlaylist([sideButtonVideo]);
            await Invoke("ShowInViewerAsync", 0, false);
            await Task.Delay(150);
            root.UpdateLayout();
            var defaultStage = (Grid)root.FindName("ViewerStage");
            Check(Math.Abs(defaultStage.ActualWidth - defaultStage.ActualHeight * 16d / 9) * root.XamlRoot.RasterizationScale < 4,
                $"默认窗口应扣除标题栏和控制栏后保留 16:9 画面，实际 {defaultStage.ActualWidth}×{defaultStage.ActualHeight}");
            foreach (bool panelOpen in new[] { false, true })
            {
                typeof(MainWindow).GetMethod("SetPlaylistOpen", flags)!.Invoke(window, [panelOpen]);
                foreach (double ratio in new[] { 16d / 9, 9d / 16, 21d / 9 })
                {
                    window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 720));
                    await Task.Delay(50);
                    root.UpdateLayout();
                    Check((bool)typeof(MainWindow).GetMethod("FitWindowToVideoAspect", flags)!.Invoke(window, [ratio])!,
                        "显式适配视频窗口应成功");
                    await Task.Delay(50);
                    root.UpdateLayout();
                    Check(Math.Abs(defaultStage.ActualWidth - defaultStage.ActualHeight * ratio) * root.XamlRoot.RasterizationScale < 4,
                        $"显式适配应完整贴合比例 {ratio}，侧栏 {panelOpen}，实际 {defaultStage.ActualWidth}×{defaultStage.ActualHeight}");
                    var fitSettings = ((AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!).Normalize();
                    Check(fitSettings.Width == window.AppWindow.Size.Width && fitSettings.Height == window.AppWindow.Size.Height
                        && fitSettings.VideoWindowDefaultsApplied, "适配后的大小（含竖屏窄窗口）必须用于浏览快照和设置保存");
                }
            }
            typeof(MainWindow).GetMethod("FitVideoWindow_Click", flags)!.Invoke(window, [root, new RoutedEventArgs()]);
            await Task.Delay(100);
            var fitPlayer = (Fallery.Services.Playback.IPlayer)Get("_videoPlayer")!;
            Check(((Button)root.FindName("FitVideoWindowButton")).IsEnabled
                && Math.Abs(defaultStage.ActualWidth - defaultStage.ActualHeight * fitPlayer.VideoWidth / fitPlayer.VideoHeight)
                    * root.XamlRoot.RasterizationScale < 4, "适配按钮必须按实际视频尺寸调整窗口");
            typeof(MainWindow).GetMethod("RotateViewerMedia", flags)!.Invoke(window, null);
            typeof(MainWindow).GetMethod("FitVideoWindow_Click", flags)!.Invoke(window, [root, new RoutedEventArgs()]);
            await Task.Delay(100);
            root.UpdateLayout();
            Check(fitPlayer.Rotation == 90 && fitPlayer.VideoWidth < fitPlayer.VideoHeight
                && Math.Abs(defaultStage.ActualWidth - defaultStage.ActualHeight * fitPlayer.VideoWidth / fitPlayer.VideoHeight)
                * root.XamlRoot.RasterizationScale < 4, "视频旋转 90° 后适配窗口必须交换宽高");
            var fittedSize = window.AppWindow.Size;
            var fittedPosition = window.AppWindow.Position;
            typeof(MainWindow).GetMethod("CloseViewer", flags)!.Invoke(window, null);
            await Task.Delay(50);
            Check(window.AppWindow.Size.Width == fittedSize.Width && window.AppWindow.Size.Height == fittedSize.Height
                && window.AppWindow.Position.X == fittedPosition.X && window.AppWindow.Position.Y == fittedPosition.Y,
                "返回文件树后必须保留适配按钮设置的新位置和大小");
            await store.SaveAsync((AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!);
            await Invoke("RestoreSettingsAsync");
            Check(window.AppWindow.Size.Width == fittedSize.Width && window.AppWindow.Size.Height == fittedSize.Height,
                "恢复已保存设置不能再次套用默认 16:9 尺寸");
            using (var cancelledFrame = new CancellationTokenSource())
            {
                cancelledFrame.Cancel();
                bool cancelled = false;
                try
                {
                    await (Task)typeof(MainWindow).GetMethod("WaitForVideoLayoutFrameAsync", flags | BindingFlags.Static)!
                        .Invoke(null, [cancelledFrame.Token])!;
                }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "切换媒体时必须能取消等待中的首帧准备");
            }
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 800));
            await Invoke("SetFilterAsync", MediaFilter.Videos, false);
            await Invoke("NavigateAsync", Path.Combine(workspace, "work", "validation", "media"), false, false);
            await Task.Delay(100);
            root.UpdateLayout();
            var previewItems = (Dictionary<UIElement, MediaItem>)Get("_realizedItems")!;
            var hoverTile = previewItems.Keys.OfType<Fallery.Controls.MediaTile>().First(tile => tile.Item?.IsVideo == true);
            var hoverItem = hoverTile.Item!;
            var hoverHost = (Grid)hoverTile.FindName("HoverVideoHost");
            void Hover(Fallery.Controls.MediaTile tile, bool entered) =>
                typeof(MainWindow).GetMethod("GalleryTile_PreviewHoverChanged", flags)!.Invoke(window, [tile, entered]);
            Hover(hoverTile, true);
            Hover(hoverTile, false);
            await Task.Delay(400);
            Check(Get("_hoverPreviewPlayer") is null && hoverHost.Children.Count == 0,
                "快速划过视频不能启动预览或残留播放表面");
            Hover(hoverTile, true);
            bool HoverIsPlaying() => hoverHost.Children.OfType<MediaPlayerElement>().FirstOrDefault()?.MediaPlayer?.PlaybackSession.PlaybackState
                == Windows.Media.Playback.MediaPlaybackState.Playing;
            for (int attempt = 0; attempt < 100 && !HoverIsPlaying(); attempt++)
                await Task.Delay(50);
            var hoverPlayer = Get("_hoverPreviewPlayer") as Fallery.Services.Playback.IPlayer;
            Check(HoverIsPlaying() && hoverPlayer is { VideoWidth: > 0, IsMuted: true, Volume: 0 } && !window.ViewerState.IsOpen,
                "悬停应在卡片内启动原生播放，始终静音且不打开查看器");
            var hoverView = hoverHost.Children.OfType<MediaPlayerElement>().Single();
            Check(!hoverView.IsHitTestVisible && !hoverView.AreTransportControlsEnabled && !hoverView.MediaPlayer.CommandManager.IsEnabled,
                "悬停预览不能拦截点击、显示控制栏或接管系统媒体按键");
            var otherHoverTile = previewItems.Keys.OfType<Fallery.Controls.MediaTile>()
                .First(tile => tile.Item?.IsVideo == true && !ReferenceEquals(tile, hoverTile));
            Hover(otherHoverTile, true);
            Check(hoverHost.Children.Count == 0 && Get("_hoverPreviewPlayer") is null
                && ReferenceEquals(Get("_hoverPreviewTile"), otherHoverTile), "移到另一个视频必须先释放前一个播放器");
            otherHoverTile.Bind(otherHoverTile.Item);
            await Task.Delay(400);
            Check(Get("_hoverPreviewPlayer") is null && Get("_hoverPreviewTile") is null,
                "卡片回收或重新绑定必须取消待启动预览，不能播放旧文件");
            Hover(hoverTile, true);
            await Invoke("ShowInViewerAsync", window.GalleryState.MediaItems.IndexOf(hoverItem), false);
            Check(Get("_hoverPreviewPlayer") is null && Get("_hoverPreviewTile") is null,
                "打开正式查看器必须终止悬停预览");
            Hover(hoverTile, true);
            Check(Get("_hoverPreviewTile") is null, "查看器打开时不能启动背后卡片的悬停预览");
            typeof(MainWindow).GetMethod("CloseViewer", flags)!.Invoke(window, null);
            Hover(hoverTile, true);
            await Invoke("SetFilterAsync", MediaFilter.All, true);
            await Invoke("NavigateAsync", folder, false, false);
            await Task.Delay(400);
            Check(Get("_hoverPreviewPlayer") is null && Get("_hoverPreviewTile") is null, "切换目录必须取消旧视频预览");
            var imageHoverTile = previewItems.Keys.OfType<Fallery.Controls.MediaTile>().First(tile => tile.Item is { IsVideo: false, IsFolder: false });
            Hover(imageHoverTile, true);
            Check(Get("_hoverPreviewTile") is null, "普通图片不能启动视频预览");
            window.ViewerState.SetPlaylist(Enumerable.Range(0, 80)
                .Select(_ => new MediaItem(Path.Combine(folder, "image1.jpg"), MediaKind.Image)).ToList());
            await Invoke("ShowInViewerAsync", 40, false);
            typeof(MainWindow).GetMethod("SetPlaylistOpen", flags)!.Invoke(window, [false]);
            typeof(MainWindow).GetMethod("SetPlaylistOpen", flags)!.Invoke(window, [true]);
            await Task.Delay(200);
            root.UpdateLayout();
            var playlist = (ListView)root.FindName("PlaylistView");
            var currentRow = (ListViewItem)playlist.ContainerFromIndex(40);
            Check(currentRow is not null && Math.Abs(currentRow.TransformToVisual(playlist).TransformPoint(new Windows.Foundation.Point()).Y) < 4,
                "展开播放列表时，中段的当前项应尽量靠顶部");
            var thumbnailsToggle = (ToggleButton)root.FindName("PlaylistThumbnailsToggle");
            thumbnailsToggle.IsChecked = false;
            typeof(MainWindow).GetMethod("PlaylistThumbnails_Click", flags)!.Invoke(window, [thumbnailsToggle, new RoutedEventArgs()]);
            await Task.Delay(200);
            root.UpdateLayout();
            currentRow = (ListViewItem)playlist.ContainerFromIndex(40);
            Check(currentRow is not null && !Descendants(currentRow).OfType<Image>().Any()
                && Descendants(currentRow).OfType<TextBlock>().Any(text => text.Text == "image1.jpg")
                && playlist.SelectedIndex == 40 && window.ViewerState.CurrentIndex == 40,
                "关闭缩略图应只显示文件名，保持当前播放项和选择");
            Check(Math.Abs(currentRow!.TransformToVisual(playlist).TransformPoint(new Windows.Foundation.Point()).Y) < 4,
                "缩略图切换到纯文字后仍应将当前项对齐顶部");
            var playlistSettings = (AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!;
            Check(!playlistSettings.PlaylistShowThumbnails && await store.SaveAsync(playlistSettings)
                && !(await store.LoadAsync()).PlaylistShowThumbnails, "播放列表缩略图设置应持久化");
            await Invoke("RestoreSettingsAsync");
            Check(thumbnailsToggle.IsChecked == false && ReferenceEquals(playlist.ItemTemplate, root.Resources["PlaylistTextTemplate"]),
                "启动设置恢复必须应用纯文字播放列表模板和按钮状态");
            thumbnailsToggle.IsChecked = true;
            typeof(MainWindow).GetMethod("PlaylistThumbnails_Click", flags)!.Invoke(window, [thumbnailsToggle, new RoutedEventArgs()]);
            await Task.Delay(200);
            root.UpdateLayout();
            currentRow = (ListViewItem)playlist.ContainerFromIndex(40);
            Check(currentRow is not null && Descendants(currentRow).OfType<Image>().Any(), "重新开启缩略图必须恢复预览模板");
            await Invoke("ShowInViewerAsync", 79, false);
            await Task.Delay(200);
            root.UpdateLayout();
            var lastRow = (ListViewItem)playlist.ContainerFromIndex(79);
            double lastTop = lastRow?.TransformToVisual(playlist).TransformPoint(new Windows.Foundation.Point()).Y ?? -1;
            Check(lastRow is not null && lastTop >= 0 && lastTop + lastRow.ActualHeight <= playlist.ActualHeight + 2,
                "列表末项无法顶对齐时仍须完整显示在可见区域内");
            typeof(MainWindow).GetMethod("CloseViewer", flags)!.Invoke(window, null);
            window.ViewerState.SetPlaylist(window.GalleryState.MediaItems);
            await Invoke("NavigateAsync", folder, false, false);
            var favoriteTree = (TreeView)root.FindName("FavoritesTree");
            var favoriteRoot = favoriteTree.RootNodes.Single();
            var favoriteButton = (Button)root.FindName("FavoriteButton");
            void ToggleFavorite() => typeof(MainWindow).GetMethod("Favorite_Click", flags)!.Invoke(window, [favoriteButton, new RoutedEventArgs()]);
            ToggleFavorite();
            Check(favoriteRoot.Children.Count == 1 && favoriteRoot.IsExpanded
                && ((FolderEntry)favoriteRoot.Children[0].Content).Path == folder
                && ((FontIcon)root.FindName("FavoriteIcon")).Glyph == Fallery.Controls.FluentIcons.Glyph("IconBookmarkFilled"),
                "地址栏书签应添加当前目录，在收藏树显示并切换实心图标");
            ToggleFavorite();
            Check(favoriteRoot.Children.Count == 0 && Directory.Exists(folder), "再次点击书签只应取消收藏，不删除目录");
            ToggleFavorite();
            var favoriteSettings = (AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!;
            Check(await store.SaveAsync(favoriteSettings) && (await store.LoadAsync()).FavoriteFolders.SequenceEqual([folder]),
                "收藏必须通过现有设置文件跨启动保存");
            await Invoke("RestoreSettingsAsync");
            Check(favoriteRoot.Children.Count == 1 && favoriteRoot.IsExpanded, "恢复设置必须重建并展开收藏入口");
            var favoriteMenu = (MenuFlyout)typeof(MainWindow).GetMethod("BuildFavoriteMenu", flags)!.Invoke(window, [folder])!;
            Check(favoriteMenu.Items.OfType<MenuFlyoutItem>().Select(item => item.Tag).SequenceEqual(new[] { "exclude", "remove-favorite" }),
                "收藏右键提供排除和移除收藏，不能提供磁盘删除操作");
            await Invoke("NavigateAsync", sideButtonFolder, false, false);
            await Invoke("OpenFavoriteAsync", folder);
            Check(window.GalleryState.CurrentFolder == folder && favoriteTree.SelectedNode == favoriteRoot.Children[0]
                && tree.SelectedNode?.Content is FolderEntry located && located.Path.Equals(folder, StringComparison.OrdinalIgnoreCase),
                "收藏跳转必须同步地址、媒体目录和真实目录树定位");
            await Invoke("OpenFavoriteAsync", Path.Combine(folder, "missing-favorite"));
            Check(window.GalleryState.CurrentFolder == folder && favoriteRoot.Children.Count == 1,
                "收藏路径失效时保持原目录，不能自动移除收藏");
            favoriteTree.SelectedNode = favoriteRoot;
            await Invoke("DeleteFocusedItemAsync", true, favoriteTree);
            Check(favoriteRoot.Children.Count == 1, "收藏分组标题的 Delete 不能影响任何目录或收藏");
            favoriteTree.SelectedNode = favoriteRoot.Children[0];
            await Invoke("DeleteFocusedItemAsync", true, favoriteTree);
            Check(favoriteRoot.Children.Count == 0 && Directory.Exists(folder) && window.GalleryState.Items.Count == 2,
                "收藏树的 Delete/Shift+Delete 只能移除入口，磁盘目录及正在浏览的内容不变");
            var normalizedFavorites = new AppSettings { FavoriteFolders = [folder, folder.ToUpperInvariant() + "\\", "relative", "E:\\invalid\0path"] }.Normalize();
            Check(normalizedFavorites.FavoriteFolders.SequenceEqual([folder])
                && new AppSettings { FavoriteFolders = null! }.Normalize().FavoriteFolders.Length == 0,
                "收藏设置应标准化路径、忽略大小写去重，并过滤非法路径和 null");
            var treeFixture = Path.Combine(workspace, "work", "validation", "tree-options", Guid.NewGuid().ToString("N"));
            string treeOne = Path.Combine(treeFixture, "folder1"), treeTwo = Path.Combine(treeFixture, "folder2"), treeTen = Path.Combine(treeFixture, "folder10");
            await Task.Run(() =>
            {
                Directory.CreateDirectory(treeOne);
                Directory.CreateDirectory(Path.Combine(treeTwo, "child"));
                Directory.CreateDirectory(treeTen);
                Directory.SetLastWriteTimeUtc(treeOne, new DateTime(2026, 3, 3, 12, 0, 0, DateTimeKind.Utc));
                Directory.SetLastWriteTimeUtc(treeTwo, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
                Directory.SetLastWriteTimeUtc(treeTen, new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc));
            });
            await Invoke("NavigateAsync", treeTwo, false, false);
            await Invoke("SelectCurrentFolderAsync", treeTwo, CancellationToken.None);
            var selectedTreeNode = tree.SelectedNode!;
            var treeParent = selectedTreeNode.Parent!;
            var preservedChild = selectedTreeNode.Children.Single();
            string[] TreeNames() => treeParent.Children.Select(node => ((FolderEntry)node.Content).Name).ToArray();
            await Invoke("SetTreeOptionsAsync", MediaSortField.Name, false, true);
            Check(TreeNames().SequenceEqual(new[] { "folder1", "folder2", "folder10" }), "文件树名称升序应使用自然排序");
            root.UpdateLayout();
            var datedEntry = (FolderEntry)selectedTreeNode.Content;
            var datedContainer = tree.ContainerFromNode(selectedTreeNode);
            Check(datedEntry.ModifiedTicks == Directory.GetLastWriteTimeUtc(treeTwo).Ticks && datedEntry.ModifiedText.Length > 0
                && datedContainer is not null && Descendants(datedContainer).OfType<TextBlock>().Any(text => text.Text == datedEntry.ModifiedText && text.Visibility == Visibility.Visible),
                "打开修改时间开关后，真实树节点应显示后台读取的修改时间");
            await Invoke("SetTreeOptionsAsync", MediaSortField.Modified, false, true);
            Check(TreeNames().SequenceEqual(new[] { "folder2", "folder10", "folder1" }), "文件树应支持修改时间升序");
            await Invoke("SetTreeOptionsAsync", MediaSortField.Modified, true, false);
            Check(TreeNames().SequenceEqual(new[] { "folder1", "folder10", "folder2" })
                && ((FolderEntry)selectedTreeNode.Content).ModifiedText == "", "时间显示关闭后仍应按修改时间降序排序");
            Check(ReferenceEquals(tree.SelectedNode, selectedTreeNode) && selectedTreeNode.IsExpanded
                && ReferenceEquals(selectedTreeNode.Children.Single(), preservedChild), "排序和显示切换不能丢失当前选择或重建展开的子树");
            await Invoke("SetTreeOptionsAsync", MediaSortField.Name, true, false);
            Check(TreeNames().SequenceEqual(new[] { "folder10", "folder2", "folder1" }), "文件树名称降序应独立于右侧媒体排序");
            var treeSettings = (AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!;
            Check(await store.SaveAsync(treeSettings) && (await store.LoadAsync()).TreeDescending, "文件树排序选项必须保存");
            await Invoke("RestoreSettingsAsync");
            Check(((RadioMenuFlyoutItem)root.FindName("TreeSortNameItem")).IsChecked
                && ((RadioMenuFlyoutItem)root.FindName("TreeDescendingItem")).IsChecked
                && ((ToggleButton)root.FindName("TreeModifiedToggle")).IsChecked == false, "文件树工具栏必须恢复已保存的状态");
            string createdFolder = await FolderCreation.CreateAsync(treeFixture, "新建目录");
            Check(Directory.Exists(createdFolder) && Path.GetDirectoryName(createdFolder) == treeFixture, "新建文件夹只能创建指定父目录中的一层目录");
            bool duplicateRejected = false;
            try { await FolderCreation.CreateAsync(treeFixture, "新建目录"); }
            catch (IOException) { duplicateRejected = true; }
            Check(duplicateRejected && Directory.Exists(createdFolder), "新建时重名必须报错且保留原目录");
            foreach (string invalidName in new[] { "..", "bad/name", "CON", "LPT1.txt", "tail.", "tail " })
            {
                bool invalidRejected = false;
                try { await FolderCreation.CreateAsync(treeFixture, invalidName); }
                catch (ArgumentException) { invalidRejected = true; }
                Check(invalidRejected, $"必须拒绝无效的新目录名称 {invalidName}");
            }
            string missingParent = Path.Combine(treeFixture, "missing-parent");
            bool missingRejected = false;
            try { await FolderCreation.CreateAsync(missingParent, "child"); }
            catch (IOException) { missingRejected = true; }
            Check(missingRejected && !Directory.Exists(missingParent), "父目录不存在时不能递归重建路径");
            await Invoke("NavigateAsync", createdFolder, false, false);
            await Invoke("SelectCurrentFolderAsync", createdFolder, CancellationToken.None);
            Check(tree.SelectedNode?.Content is FolderEntry createdEntry && createdEntry.Path == createdFolder
                && createdEntry.ModifiedTicks.HasValue && ((Button)root.FindName("NewFolderButton")).IsEnabled,
                "新建空目录应立即可在树中定位，并具有修改时间和可用的新建按钮");
            Check(new AppSettings { TreeSortField = MediaSortField.Size }.Normalize().TreeSortField == MediaSortField.Name,
                "非法文件树排序字段必须回退名称");
            void Exclude(string path) => typeof(MainWindow).GetMethod("ExcludeTreeFolder", flags)!.Invoke(window, [path]);
            bool Excluded(string path) => (bool)typeof(MainWindow).GetMethod("IsTreeFolderExcluded", flags)!.Invoke(window, [path])!;
            await Invoke("NavigateAsync", treeTwo, false, false);
            await Invoke("SelectCurrentFolderAsync", treeTwo, CancellationToken.None);
            ToggleFavorite();
            Exclude(treeTwo);
            Check(!TreeNames().Contains("folder2") && tree.SelectedNode is null && Directory.Exists(treeTwo)
                && window.GalleryState.CurrentFolder == treeTwo, "排除应立即移除节点和选择，保留磁盘目录及当前浏览内容");
            Check(Excluded(Path.Combine(treeTwo, "child")) && !Excluded(treeTen), "排除必须涵盖后代但不能误伤相邻名称目录");
            Check(!favoriteRoot.Children.Any(node => ((FolderEntry)node.Content).Path == treeTwo), "排除应同时隐藏收藏入口");
            await Invoke("SelectCurrentFolderAsync", treeTen, CancellationToken.None);
            await Invoke("SelectCurrentFolderAsync", Path.Combine(treeTwo, "child"), CancellationToken.None);
            Check(!TreeNames().Contains("folder2") && tree.SelectedNode is null, "显式路径定位不能重新插入排除的祖先目录或保留其他目录的高亮");
            await Invoke("RefreshWatchedTreeAsync", treeFixture, (int)Get("_navigationVersion")!);
            Check(!TreeNames().Contains("folder2"), "自动刷新不能恢复排除的目录");
            var excludedSettings = (AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(window, null)!;
            Check(await store.SaveAsync(excludedSettings) && (await store.LoadAsync()).TreeExcludedFolders.SequenceEqual([treeTwo]), "排除路径必须跨启动保存");
            await Invoke("RestoreSettingsAsync");
            Check(Excluded(treeTwo), "启动恢复设置后必须保持排除规则");
            Exclude(treeFixture);
            await Invoke("RestoreExcludedTreeFolderAsync", treeTwo);
            Check(Excluded(treeTwo), "取消子目录排除时仍应遵守父目录排除规则");
            await Invoke("RestoreExcludedTreeFolderAsync", treeFixture);
            await Invoke("SelectCurrentFolderAsync", treeTwo, CancellationToken.None);
            Check(tree.SelectedNode?.Content is FolderEntry restoredTree && restoredTree.Path == treeTwo
                && favoriteRoot.Children.Any(node => ((FolderEntry)node.Content).Path == treeTwo), "取消父目录排除后应恢复节点和保留的收藏");
            Check(new AppSettings { TreeExcludedFolders = [treeTwo, treeTwo.ToUpperInvariant() + "\\", "relative"] }.Normalize()
                .TreeExcludedFolders.SequenceEqual([treeTwo]), "排除路径应标准化去重并忽略无效路径");
            var excludedRoot = tree.RootNodes.First(node => node.Content is FolderEntry entry && IsAncestor(entry.Path, treeTwo));
            string excludedRootPath = ((FolderEntry)excludedRoot.Content).Path;
            Exclude(excludedRootPath);
            Check(!tree.RootNodes.Contains(excludedRoot), "排除根入口也应立即隐藏");
            await Invoke("RestoreExcludedTreeFolderAsync", excludedRootPath);
            Check(tree.RootNodes.Any(node => node.Content is FolderEntry entry && entry.Path == excludedRootPath), "取消排除根入口必须恢复导航");
            static bool IsAncestor(string ancestor, string path) => path.StartsWith(ancestor.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            typeof(MainWindow).GetMethod("ApplyTheme", flags)!.Invoke(window, new object[] { 1 });
            ((Slider)root.FindName("ThumbSize")).Value = 360;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            // 只向本测试创建的未激活窗口请求正常关闭，不查找或操作用户窗口。
            Check(PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(window), 0x0010, 0, 0), "无法请求测试窗口关闭");
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            window = null;
            saved = await store.LoadAsync();
            Check(saved.Theme == 1 && saved.ThumbnailSize == 360, "正常关闭没有等待最后一次设置保存");
            File.WriteAllText(report, $"PASS: {checks} production window settings/navigation/sorting checks; no window activation or user input.");
        }
        catch (Exception ex)
        {
            File.AppendAllText(report, $"\nFAIL: {ex}");
            Environment.ExitCode = 1;
        }
        finally { window?.Close(); }
        Exit();
    }

    private sealed class MouseSeekPlayer : Fallery.Services.Playback.IPlayer
    {
        public event EventHandler? Opened { add { } remove { } }
        public event EventHandler? Ended { add { } remove { } }
        public event EventHandler? StateChanged { add { } remove { } }
        public event EventHandler<string>? Failed { add { } remove { } }
        public event EventHandler? TracksChanged { add { } remove { } }
        public TimeSpan Position { get; set; } = TimeSpan.FromSeconds(20);
        public TimeSpan Duration => TimeSpan.FromSeconds(60);
        public uint VideoWidth => 1920;
        public uint VideoHeight => 1080;
        public int Rotation { get; set; }
        public bool IsPlaying { get; private set; }
        public double Volume { get; set; } = 100;
        public bool IsMuted { get; set; }
        public double Rate { get; set; } = 1;
        public IReadOnlyList<string> AudioTracks => [];
        public int AudioTrack { get; set; }
        public Task OpenAsync(string path, CancellationToken ct) => Task.CompletedTask;
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void StepFrame(bool forward) { }
        public void Dispose() { }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
