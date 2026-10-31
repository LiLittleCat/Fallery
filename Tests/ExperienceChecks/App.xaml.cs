using System.Reflection;
using System.Runtime.InteropServices;
using Fallery.Models;
using Fallery.Services;
using Fallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
            await store.SaveAsync(new AppSettings { Width = 1100, Height = 720, Theme = 2, ThumbnailSize = 320,
                SidebarWidth = 300, SortField = MediaSortField.Size, Descending = true });
            // 直接实例化生产窗口，不激活、不发送输入，设置写入隔离的测试目录。
            window = new MainWindow(store);
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
            Check(((ComboBox)root.FindName("ThemeSelector")).SelectedIndex == 2, "实际主题控件没有恢复");
            Check(((Slider)root.FindName("ThumbSize")).Value == 320, "实际缩略图控件没有恢复");
            var folder = Path.Combine(workspace, "work", "validation", "experience", "01-图片");
            await Invoke("NavigateAsync", folder, false, false);
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
            Check(((TextBlock)nameless.FindName("MediaName")).Visibility == Visibility.Collapsed
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
            await Invoke("ShowInViewerAsync", 1);
            root.UpdateLayout();
            var selectedTile = (Fallery.Controls.MediaTile)galleryControl.TryGetElement(1);
            Check(window.ViewerState.CurrentIndex == 1
                && ((Border)selectedTile.FindName("SelectionOutline")).Visibility == Visibility.Visible,
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
            var imageMenu = (MenuFlyout)typeof(MainWindow).GetMethod("BuildItemMenu", flags)!.Invoke(window, new object?[] { items[0].Path, false, null })!;
            Check(imageMenu.Items.OfType<MenuFlyoutItem>().Select(i => i.Tag).SequenceEqual(new[] { "open", "reveal", "copy-path", "copy-file" }),
                "媒体菜单应提供打开、定位和复制操作");
            var treeMenu = (MenuFlyout)typeof(MainWindow).GetMethod("BuildItemMenu", flags)!.Invoke(window, new object?[] { folder, true, firstNode })!;
            Check(treeMenu.Items.OfType<MenuFlyoutItem>().Any(i => Equals(i.Tag, "expand"))
                && treeMenu.Items.OfType<MenuFlyoutItem>().Any(i => Equals(i.Tag, "refresh")), "文件树菜单缺少展开或刷新入口");
            await Invoke("SetDisplayModeAsync", GalleryDisplayMode.Masonry);
            namesToggle.IsChecked = true;
            typeof(MainWindow).GetMethod("ShowFileNames_Click", flags)!.Invoke(window, new object[] { namesToggle, new RoutedEventArgs() });
            root.UpdateLayout();
            Check(((Grid)root.FindName("Viewer")).Parent == root && !window.ViewerState.IsOpen
                && galleryControl.Layout is Fallery.Layouts.MasonryLayout,
                "返回瀑布流必须归还查看器覆盖层并恢复原虚拟化布局");
            Check(window.GalleryState.Items.All(original.Contains), "切换视图不能重扫目录或重建媒体模型");
            window.GalleryState.SortField = MediaSortField.Name;
            window.GalleryState.Descending = false;
            await Invoke("ApplySortAsync");
            items = window.GalleryState.Items;
            Check(items.Select(i => i.Name).SequenceEqual(new[] { "image1.jpg", "image2.jpg" }), "实际排序操作未更新数据顺序");
            Check(items.All(original.Contains), "排序不能重建媒体模型丢掉缓存");
            Check(window.ViewerState.Playlist.SequenceEqual(items), "查看器播放顺序未与排序同步");
            ((ComboBox)root.FindName("ThemeSelector")).SelectedIndex = 1;
            root.UpdateLayout();
            Check(window.AppWindow.TitleBar.PreferredTheme == Microsoft.UI.Windowing.TitleBarTheme.Light,
                "亮色模式的系统窗口按钮未同步");
            var request = window.ViewerState.BeginOpen(0)!;
            Check(window.ViewerState.Title == items[0].Name && window.ViewerState.CanNext && !window.ViewerState.CanPrevious,
                "查看器状态没有从当前项派生");
            Check(((TextBlock)root.FindName("ViewerTitle")).Text == items[0].Name
                && ((Grid)root.FindName("Viewer")).Visibility == Visibility.Visible, "查看器标题或显示状态绑定未更新");
            Check(((Grid)root.FindName("GalleryToolbar")).Visibility == Visibility.Collapsed
                && ((Grid)root.FindName("ViewerToolbar")).Visibility == Visibility.Visible
                && ((TitleBar)root.FindName("AppTitleBar")).RequestedTheme == ElementTheme.Dark, "查看器应切换顶部栏并保持深色");
            window.ViewerState.SetPlaylist(items.AsEnumerable().Reverse().ToList());
            Check(ReferenceEquals(window.ViewerState.CurrentItem, items[0]) && window.ViewerState.CurrentIndex == 1,
                "重排播放列表必须保持当前媒体");
            Check(((TextBlock)root.FindName("ViewerCounter")).Text == "2 / 2" && !((Button)root.FindName("NextButton")).IsEnabled,
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
            ((ComboBox)root.FindName("ThemeSelector")).SelectedIndex = 2;
            window.ViewerState.SetPlaylist(window.GalleryState.MediaItems);
            Check(items.All(i => i.Version is { } key && MediaCache.AspectRatios.TryGet(key, out _)),
                "目录探测没有把宽高比保留在内存");
            bool ratiosReadyAtCommit = false;
            window.GalleryState.FolderCommitted += (_, _) => ratiosReadyAtCommit = window.GalleryState.MediaItems.All(i => i.HasAspectRatio);
            await Invoke("NavigateAsync", folder, false, true);
            Check(ratiosReadyAtCommit, "内存宽高比应在首次布局前恢复");
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
                if (saved.LastFolder == folder) break;
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
            ((ComboBox)root.FindName("ThemeSelector")).SelectedIndex = 1;
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
