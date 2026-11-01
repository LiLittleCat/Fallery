using System.Reflection;
using Fallery.Controls;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunResizePreviewChecksAsync(MainWindow window, string workspace)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var root = (FrameworkElement)window.Content;
        var gallery = (ItemsRepeater)root.FindName("Gallery");
        string fixture = Path.Combine(workspace, "work", "validation", "resize-previews", Guid.NewGuid().ToString("N"));
        for (int i = 0; i < 36; i++)
        {
            string folder = Directory.CreateDirectory(Path.Combine(fixture, $"folder{i:00}")).FullName;
            for (int j = 0; j < 3; j++)
                File.Copy(Path.Combine(workspace, "work", "validation", "media", "image1.jpg"), Path.Combine(folder, $"{j}.jpg"));
        }
        Call("CloseViewer");
        window.GalleryState.ShowSubfolders = true;
        window.GalleryState.Filter = MediaFilter.All;
        await (Task)Call("SetDisplayModeAsync", GalleryDisplayMode.LargeIcons)!;
        await (Task)Call("NavigateAsync", fixture, false, false)!;
        async Task Resize(int width, int height)
        {
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
            await Task.Delay(150); root.UpdateLayout();
        }
        await Resize(1600, 900);
        var items = window.GalleryState.Items;
        uint size = (uint)typeof(MainWindow).GetProperty("ThumbnailDecodeSize", flags)!.GetValue(window)!;
        await Task.WhenAll(items.Select(item => item.EnsureThumbnailAsync(size)));
        Check(items.Count == 36 && items.All(item => item.FolderThumbnails.Count == 3), "调整窗口检查需要已完成的三图文件夹封面");
        var snapshots = items.ToDictionary(item => item, item => item.FolderThumbnails.ToArray());
        int recycled = 0, restored = 0, missing = 0;
        void Cleared(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args) => recycled++;
        void Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
        {
            if (args.Element is not MediaTile { Item: { } item } || !snapshots.TryGetValue(item, out var covers)) return;
            restored++;
            if (!item.FolderThumbnails.SequenceEqual(covers)
                || !ReferenceEquals(((Image)((MediaTile)args.Element).FindName("FrontCoverImage")).Source, covers[0])) missing++;
        }
        gallery.ElementClearing += Cleared;
        gallery.ElementPrepared += Prepared;
        try
        {
            await Resize(650, 450);
            await Resize(1600, 900);
            Check(recycled > 0 && restored > 0, "改变窗口必须实际覆盖卡片回收与重现路径");
            Check(missing == 0, $"调整窗口后 {missing}/{restored} 张文件夹卡片未同步恢复已有封面");
            Check(ReferenceEquals(items, window.GalleryState.Items), "调整窗口不能重新扫描目录或替换列表");
        }
        finally
        {
            gallery.ElementClearing -= Cleared;
            gallery.ElementPrepared -= Prepared;
        }
        string mediaFolder = Directory.CreateDirectory(Path.Combine(fixture, "media")).FullName;
        for (int i = 0; i < 36; i++)
        {
            string source = i % 2 == 0 ? "image1.jpg" : "video.mp4";
            File.Copy(Path.Combine(workspace, "work", "validation", "media", source),
                Path.Combine(mediaFolder, $"{i:00}" + Path.GetExtension(source)));
        }
        await (Task)Call("NavigateAsync", mediaFolder, false, false)!;
        root.UpdateLayout();
        items = window.GalleryState.Items;
        await Task.WhenAll(items.Select(item => item.EnsureThumbnailAsync(size)));
        Check(items.Count == 36 && items.All(item => item.Thumbnail is not null), "调整窗口检查需要已完成的图片和视频预览");
        var mediaSnapshots = items.ToDictionary(item => item, item => item.Thumbnail);
        recycled = restored = missing = 0;
        void MediaPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
        {
            if (args.Element is not MediaTile { Item: { } item } tile || !mediaSnapshots.TryGetValue(item, out var bitmap)) return;
            restored++;
            if (!ReferenceEquals(item.Thumbnail, bitmap)
                || !ReferenceEquals(((Image)tile.FindName("PreviewImage")).Source, bitmap)) missing++;
        }
        gallery.ElementClearing += Cleared;
        gallery.ElementPrepared += MediaPrepared;
        try
        {
            await Resize(650, 450);
            await Resize(1600, 900);
            Check(recycled > 0 && restored > 0, "改变窗口必须覆盖图片/视频卡片回收路径");
            Check(missing == 0, $"调整窗口后 {missing}/{restored} 张媒体卡片未同步恢复已有预览");
            Check(ReferenceEquals(items, window.GalleryState.Items), "调整窗口不能重新扫描媒体目录");
        }
        finally
        {
            gallery.ElementClearing -= Cleared;
            gallery.ElementPrepared -= MediaPrepared;
        }
        return checks;
    }
}
