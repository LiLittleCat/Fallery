using System.Reflection;
using Fallery.Controls;
using Fallery.Models;
using Fallery.Layouts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunListViewChecksAsync(MainWindow window, string workspace)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        async Task Invoke(string name, params object[] args) => await (Task)Call(name, args)!;
        var root = (FrameworkElement)window.Content;
        var gallery = (ItemsRepeater)root.FindName("Gallery");
        var scroller = (ScrollViewer)root.FindName("GalleryScroller");
        var header = (FrameworkElement)root.FindName("DetailsHeader");
        string fixture = Directory.CreateDirectory(Path.Combine(workspace, "work", "validation", "list-view", Guid.NewGuid().ToString("N"))).FullName;
        for (int i = 0; i < 160; i++)
            File.Copy(Path.Combine(workspace, "work", "validation", "media", "image1.jpg"), Path.Combine(fixture, $"photo{i:000}.jpg"));
        string child = Directory.CreateDirectory(Path.Combine(fixture, "album")).FullName;
        File.Copy(Path.Combine(workspace, "work", "validation", "media", "image2.jpg"), Path.Combine(child, "cover.jpg"));
        Call("CloseViewer");
        window.GalleryState.ShowSubfolders = true;
        window.GalleryState.Filter = MediaFilter.All;
        await Invoke("SetDisplayModeAsync", GalleryDisplayMode.Details);
        await Invoke("NavigateAsync", fixture, false, false);
        async Task Settle() { root.UpdateLayout(); await Task.Delay(120); root.UpdateLayout(); }
        await Settle();
        Check(header.Visibility == Visibility.Visible && ((RadioMenuFlyoutItem)root.FindName("DetailsModeItem")).IsChecked,
            "列表视图应显示列头和视图选中项");
        Check(!((Slider)root.FindName("ThumbSize")).IsEnabled && !((ToggleMenuFlyoutItem)root.FindName("ShowFileNamesItem")).IsEnabled,
            "列表行保持小图及名称，不受卡片开关影响");
        var first = (MediaTile)gallery.TryGetElement(0);
        Check(first is not null && first.ActualHeight == 60 && ((Grid)first.FindName("ListContent")).Visibility == Visibility.Visible,
            "列表应使用紧凑固定高度的真实行控件");
        Check(first!.Item!.IsFolder && ((TextBlock)first.FindName("ListType")).Text == "文件夹"
            && ((TextBlock)first.FindName("ListSize")).Text == "—", "列表中文件夹优先，大小不递归估算");
        var second = (MediaTile)gallery.TryGetElement(1);
        var item = second.Item!;
        Check(((TextBlock)second.FindName("ListName")).Text == item.Name
            && ((TextBlock)second.FindName("ListType")).Text == "JPG 图片"
            && ((TextBlock)second.FindName("ListSize")).Text == item.File.SizeText
            && ((TextBlock)second.FindName("ListCreated")).Text == item.File.CreatedText
            && ((TextBlock)second.FindName("ListModified")).Text == item.File.ModifiedText, "列表真实行绑定全部文件元数据");
        await item.EnsureThumbnailAsync(128);
        Check(ReferenceEquals(((Image)second.FindName("ListPreviewImage")).Source, item.Thumbnail) && item.Thumbnail is not null,
            "列表小预览应绑定生产缩略图");
        var before = window.GalleryState.Items;
        Call("ApplyItemSelection", before[1], false, false);
        Call("ApplyItemSelection", before[3], false, true);
        Check(((HashSet<string>)typeof(MainWindow).GetField("_selectedPaths", flags)!.GetValue(window)!).Count == 3,
            "列表模式沿用 Shift 多选");
        Call("ApplyItemSelection", before[2], true, false);
        Check(((HashSet<string>)typeof(MainWindow).GetField("_selectedPaths", flags)!.GetValue(window)!).Count == 2,
            "列表模式沿用 Ctrl 切换选择");
        await Invoke("SetDetailsSortAsync", MediaSortField.Created);
        await Invoke("SetDetailsSortAsync", MediaSortField.Created);
        Check(window.GalleryState.SortField == MediaSortField.Created && window.GalleryState.Descending
            && ((Button)root.FindName("DetailsCreatedHeader")).Content.ToString()!.EndsWith("↓"), "点击列表列头切换排序方向并更新标记");
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(750, 600));
        await Settle();
        scroller.ChangeView(160, 1200, null, true);
        await Settle();
        Check(scroller.ScrollableWidth > 0 && scroller.HorizontalOffset > 0, "窄窗口列表必须支持横向滚动");
        Check(Math.Abs(((ScrollViewer)root.FindName("DetailsHeaderScroller")).HorizontalOffset - scroller.HorizontalOffset) < 1,
            "横向滚动时列头必须与内容同步");
        var visibleRow = window.GalleryState.Items.Select((_, index) => gallery.TryGetElement(index)).OfType<MediaTile>().First();
        double columnX = ((FrameworkElement)visibleRow.FindName("ListCreated")).TransformToVisual(root).TransformPoint(new()).X;
        double headerX = ((FrameworkElement)root.FindName("DetailsCreatedHeader")).TransformToVisual(root).TransformPoint(new()).X;
        Check(Math.Abs(columnX - headerX) < 1, $"列表列头与真实行列位置应对齐：{columnX} / {headerX}");
        Check(header.TransformToVisual(root).TransformPoint(new()).Y < scroller.TransformToVisual(root).TransformPoint(new()).Y,
            "纵向滚动时列头保持在列表顶部");
        int realized = window.GalleryState.Items.Select((_, i) => gallery.TryGetElement(i)).Count(element => element is not null);
        Check(realized > 0 && realized < 80, $"160 行列表不能全部创建控件，实际 {realized}");
        var current = window.GalleryState.Items.Skip(1).First(entry => !entry.IsFolder);
        Call("SelectItem", current);
        Call("BringItemIntoView", current);
        await Settle();
        await Invoke("ShowInViewerAsync", window.GalleryState.MediaItems.IndexOf(current), false);
        Check(window.ViewerState.IsOpen && window.ViewerState.CurrentItem == current, "列表中的文件可使用原查看器打开");
        Call("CloseViewer");
        await Settle();
        Check(!window.ViewerState.IsOpen && ReferenceEquals(typeof(MainWindow).GetField("_selectedItem", flags)!.GetValue(window), current),
            "关闭查看器返回列表仍选中刚打开的文件");
        await Invoke("SetDisplayModeAsync", GalleryDisplayMode.Masonry);
        await Settle();
        Check(header.Visibility == Visibility.Collapsed && double.IsNaN(gallery.Width) && scroller.Margin.Top == 0,
            "切回原视图必须释放列表宽度和列头占位");
        Check(((Grid)((MediaTile)gallery.TryGetElement(0)).FindName("TileContent")).Visibility == Visibility.Visible,
            "列表复用回普通卡片时恢复原布局");
        return checks;
    }
}
