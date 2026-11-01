using System.Reflection;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunBrowsePositionChecksAsync(MainWindow window, string workspace)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        async Task Invoke(string name, params object[] args) => await ((Task)Call(name, args)!).WaitAsync(TimeSpan.FromSeconds(20));
        var root = (FrameworkElement)window.Content;
        var scroll = (ScrollViewer)root.FindName("GalleryScroller");
        async Task Settle() { root.UpdateLayout(); await Task.Delay(150); root.UpdateLayout(); }
        var directory = Directory.CreateDirectory(Path.Combine(workspace, "work", "validation", "browse-position")).FullName;
        var source = Path.Combine(workspace, "work", "validation", "media", "image1.jpg");
        for (int i = 0; i < 120; i++)
        {
            string target = Path.Combine(directory, $"image{i:D3}.jpg");
            if (!File.Exists(target)) File.Copy(source, target);
        }
        for (int i = 0; i < 40; i++)
        {
            string child = Directory.CreateDirectory(Path.Combine(directory, $"folder{i:D3}")).FullName;
            string target = Path.Combine(child, "image.jpg");
            if (!File.Exists(target)) File.Copy(source, target);
        }
        Call("CloseViewer");
        window.GalleryState.Filter = MediaFilter.All;
        window.GalleryState.ShowSubfolders = true;
        window.GalleryState.SortField = MediaSortField.Name;
        window.GalleryState.Descending = false;
        window.GalleryState.DisplayMode = GalleryDisplayMode.Justified;
        Call("ApplyDisplayOptions");
        await Invoke("NavigateAsync", directory, true, false);
        await Settle();
        var childItem = window.GalleryState.Items.Last(item => item.IsFolder);
        Call("SelectItem", childItem);
        Call("BringItemIntoView", childItem);
        await Settle();
        double parentOffset = scroll.VerticalOffset;
        Check(parentOffset > 100, "返回位置回归必须从已滚动的目录开始");
        await Invoke("NavigateAsync", childItem.Path, true, false);
        await Invoke("GoBackAsync");
        await Settle();
        MediaItem? Selected() => (MediaItem?)typeof(MainWindow).GetField("_selectedItem", flags)!.GetValue(window);
        Check(Selected()?.Path == childItem.Path && Math.Abs(scroll.VerticalOffset - parentOffset) < 3,
            $"后退必须恢复父目录的选中子目录和位置：期望 {parentOffset}，实际 {scroll.VerticalOffset}");
        await Invoke("NavigateAsync", childItem.Path, true, false);
        await Invoke("GoUpAsync");
        await Settle();
        Check(Selected()?.Path == childItem.Path && Math.Abs(scroll.VerticalOffset - parentOffset) < 3,
            "上级必须恢复刚进入的子目录和原来的浏览位置");
        var image = window.GalleryState.MediaItems[100];
        Call("SelectItem", image);
        Call("BringItemIntoView", image);
        await Settle();
        double imageOffset = scroll.VerticalOffset;
        await Invoke("ShowInViewerAsync", 100, false);
        await Settle();
        Call("CloseViewer");
        await Settle();
        Check(Selected()?.Path == image.Path && Math.Abs(scroll.VerticalOffset - imageOffset) < 3,
            $"关闭图片必须恢复该图片和滚动位置：期望 {imageOffset}，实际 {scroll.VerticalOffset}");
        await Invoke("ShowInViewerAsync", 100, false);
        await Invoke("ShowInViewerAsync", 0, false);
        Call("CloseViewer");
        await Settle();
        Check(Selected()?.Path == window.GalleryState.MediaItems[0].Path && scroll.VerticalOffset < imageOffset,
            "查看器切到另一屏图片后返回，应定位最后查看的图片");
        await Invoke("NavigateAsync", childItem.Path, true, false);
        var positions = typeof(MainWindow).GetField("_browsePositions", flags)!.GetValue(window)!;
        positions.GetType().GetMethod("Clear")!.Invoke(positions, null);
        await Invoke("GoUpAsync");
        await Settle();
        Check(Selected()?.Path == childItem.Path && scroll.VerticalOffset > 100,
            "没有父目录位置快照时，上级仍应选中并滚到刚离开的子目录");
        return checks;
    }
}
