using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Fallery.TileChecks;

public partial class App : Application
{
    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args) => _ = RunChecksAsync();

    private async Task RunChecksAsync()
    {
        var report = System.IO.Path.Combine(AppContext.BaseDirectory, "tile-checks.txt");
        try
        {
            int checks = 0;
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
                checks++;
            }

            // 直接实例化生产控件验证复用，不发送鼠标键盘输入，也不读取用户窗口。
            var tile = new MediaTile();
            var name = (TextBlock)tile.FindName("FolderName");
            var mediaName = (TextBlock)tile.FindName("MediaName");
            var footer = (Border)tile.FindName("FolderFooter");
            var placeholder = (FontIcon)tile.FindName("FolderPlaceholder");
            // 显示文件名时视频标记位于文件名末尾。
            var badge = (FontIcon)tile.FindName("VideoMark");
            var image = (Image)tile.FindName("PreviewImage");
            var failure = (TextBlock)tile.FindName("FailureText");
            MediaItem? previous = null;
            for (int i = 0; i < 90; i++)
            {
                var kind = (MediaKind)(i % 3);
                var item = new MediaItem($@"E:\tile-checks\directory{i}\item{i}", kind);
                tile.Bind(item);
                Check(ReferenceEquals(tile.Item, item), "点击目标未切换到新对象");
                Check(name.Text == (item.IsFolder ? item.Name : ""), "旧目录名称残留");
                Check(mediaName.Text == (item.IsFolder ? "" : item.Name), "媒体文件名没有跟随复用更新");
                Check(footer.Visibility == (item.IsFolder ? Visibility.Visible : Visibility.Collapsed), "文件夹名称区域未重置");
                Check(placeholder.Visibility == footer.Visibility, "文件夹占位未同步");
                Check(badge.Visibility == (item.IsVideo ? Visibility.Visible : Visibility.Collapsed), "视频角标未重置");
                Check((string?)ToolTipService.GetToolTip(tile) == (item.IsFolder ? item.Path : null), "图片和视频不能弹出路径提示，文件夹提示应跟随复用更新");
                Check(image.Source is null && failure.Visibility == Visibility.Collapsed, "旧封面或失败标记残留");
                if (previous is not null)
                {
                    previous.Thumbnail = new BitmapImage();
                    previous.FolderThumbnails = [new BitmapImage(), new BitmapImage()];
                    previous.ThumbnailFailed = true;
                    Check(image.Source is null && ((Image)tile.FindName("FrontCoverImage")).Source is null
                        && failure.Visibility == Visibility.Collapsed, "旧对象异步回调污染新卡片");
                }
                item.Thumbnail = new BitmapImage();
                if (item.IsFolder) item.FolderThumbnails = [item.Thumbnail];
                Check(ReferenceEquals(item.IsFolder ? ((Image)tile.FindName("FrontCoverImage")).Source : image.Source, item.Thumbnail),
                    "新对象封面更新未订阅");
                item.ThumbnailFailed = true;
                Check(failure.Visibility == Visibility.Visible, "新对象失败状态未订阅");
                tile.Bind(null);
                Check(tile.Item is null && image.Source is null && name.Text == "", "回收后未清空卡片");
                Check(mediaName.Text == "", "回收后媒体文件名残留");
                Check(footer.Visibility == Visibility.Collapsed && badge.Visibility == Visibility.Collapsed
                    && failure.Visibility == Visibility.Collapsed, "回收后类型或失败状态残留");
                previous = item;
            }
            MediaCache.Clear();
            var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../.."));
            var source = Path.Combine(workspace, "work/validation/media/image1.jpg");
            var fixture = Path.Combine(workspace, "work/validation/cachechecks", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            var photo = Path.Combine(fixture, "photo.jpg");
            File.Copy(source, photo);
            var version = FileVersion.Read(photo)!.Value;
            MediaCache.AspectRatios.Set(version, 16.0 / 9);
            var first = new MediaItem(photo, MediaKind.Image, version);
            Check(first.HasAspectRatio && first.AspectRatio == 16.0 / 9, "返回目录应直接恢复已知宽高比");
            using var trace = new StringWriter();
            using var listener = new System.Diagnostics.TextWriterTraceListener(trace);
            System.Diagnostics.Trace.Listeners.Add(listener);
            await first.EnsureThumbnailAsync(128);
            Check(first.Thumbnail is not null, $"真实系统缩略图加载失败: {trace}; version={version}, after={FileVersion.Read(photo)}");
            var bitmap = first.Thumbnail;
            first.ReleaseThumbnail();
            Check(first.Thumbnail is null, "回收卡片仍须清除模型引用");
            var returned = new MediaItem(photo, MediaKind.Image, version);
            var warmLoad = returned.EnsureThumbnailAsync(128);
            Check(warmLoad.IsCompletedSuccessfully && ReferenceEquals(returned.Thumbnail, bitmap), "返回目录必须同步命中已解码图片，不出现灰色等待");
            tile.Bind(returned);
            Check(ReferenceEquals(image.Source, bitmap), "缓存图片必须正确绑定到复用卡片");
            Check(MediaCache.GetThumbnail(version, 256) is null, "放大不能误用分辨率不足的缓存");
            await returned.EnsureThumbnailAsync(256);
            var larger = returned.Thumbnail;
            Check(larger is not null && !ReferenceEquals(larger, bitmap), "放大应加载更清晰的图片");
            Check(ReferenceEquals(MediaCache.GetThumbnail(version, 128), larger), "缩小时应复用较大的缓存");
            var folder = new MediaItem(fixture, MediaKind.Folder);
            await folder.EnsureThumbnailAsync(128);
            Check(ReferenceEquals(folder.Thumbnail, larger), "文件夹封面应复用媒体缓存");
            File.Copy(Path.Combine(workspace, "work/validation/media/image2.jpg"), photo, true);
            File.SetLastWriteTimeUtc(photo, DateTime.UtcNow.AddSeconds(5));
            var changed = FileVersion.Read(photo)!.Value;
            Check(changed != version && MediaCache.GetThumbnail(changed, 128) is null, "覆盖后的文件不能命中旧缩略图");
            Check(!new MediaItem(photo, MediaKind.Image, changed).HasAspectRatio, "覆盖后的文件不能命中旧宽高比");
            folder.ReleaseThumbnail();
            await folder.EnsureThumbnailAsync(128);
            Check(folder.Thumbnail is not null && !ReferenceEquals(folder.Thumbnail, larger), "文件夹时间戳未变时也必须更新被覆盖的封面");
            File.Delete(photo);
            folder.ReleaseThumbnail();
            await folder.EnsureThumbnailAsync(128);
            Check(folder.Thumbnail is null && !folder.ThumbnailFailed, "删除封面后空文件夹不能残留旧图");
            var nestedAlbum = Directory.CreateDirectory(Path.Combine(fixture, "nested", "album")).FullName;
            foreach (var mediaNameForCover in new[] { "image1.jpg", "image2.jpg", "motion.gif" })
                File.Copy(Path.Combine(workspace, "work/validation/media", mediaNameForCover), Path.Combine(nestedAlbum, mediaNameForCover));
            await folder.EnsureThumbnailAsync(128);
            Check(folder.FolderThumbnails.Count == 3 && ReferenceEquals(folder.Thumbnail, folder.FolderThumbnails[0]),
                "嵌套目录应加载三张不同文件的封面，并保留首图缓存接口");
            tile.Bind(folder);
            Check(((Grid)tile.FindName("FolderStack")).Visibility == Visibility.Visible
                && ((Border)tile.FindName("BackCover")).Visibility == Visibility.Visible
                && ReferenceEquals(((Image)tile.FindName("FrontCoverImage")).Source, folder.FolderThumbnails[0])
                && ReferenceEquals(((Image)tile.FindName("MiddleCoverImage")).Source, folder.FolderThumbnails[1])
                && ReferenceEquals(((Image)tile.FindName("BackCoverImage")).Source, folder.FolderThumbnails[2]),
                "交叉叠图预览必须绑定三张真实封面");
            tile.SetPresentation(false, true, false);
            Check(((Grid)tile.FindName("FolderArtwork")).Visibility == Visibility.Visible
                && placeholder.Visibility == Visibility.Collapsed
                && ((Border)tile.FindName("FolderFooter")).Visibility == Visibility.Collapsed,
                "有封面时只显示交叉叠图，隐藏文件名也不能出现大文件夹底形");
            folder.FolderThumbnails = [folder.FolderThumbnails[0]];
            Check(placeholder.Visibility == Visibility.Collapsed
                && ((Border)tile.FindName("MiddleCover")).Visibility == Visibility.Collapsed
                && ((Border)tile.FindName("BackCover")).Visibility == Visibility.Collapsed
                && ((Image)tile.FindName("MiddleCoverImage")).Source is null
                && ((Image)tile.FindName("BackCoverImage")).Source is null,
                "三封面变为单封面后应清除旧叠层，不能显示大文件夹底形");
            folder.ReleaseThumbnail();
            Check(folder.FolderThumbnails.Count == 0 && ((Image)tile.FindName("FrontCoverImage")).Source is null
                && ((Image)tile.FindName("MiddleCoverImage")).Source is null && ((Image)tile.FindName("BackCoverImage")).Source is null,
                "回收必须释放所有叠放图片引用");
            Check(((Grid)tile.FindName("FolderArtwork")).Visibility == Visibility.Visible
                && placeholder.Visibility == Visibility.Visible && ((Grid)tile.FindName("FolderStack")).Visibility == Visibility.Collapsed,
                "封面回收后目录应显示大文件夹，不能变成空白");
            tile.Bind(returned);
            Check(((Grid)tile.FindName("FolderStack")).Visibility == Visibility.Collapsed
                && ((Grid)tile.FindName("FolderArtwork")).Visibility == Visibility.Collapsed,
                "复用为媒体卡片时不能残留文件夹底形与叠层");
            MediaCache.Clear();
            var canceled = new MediaItem(source, MediaKind.Image, FileVersion.Read(source));
            var pending = canceled.EnsureThumbnailAsync(512);
            canceled.ReleaseThumbnail();
            tile.Bind(new MediaItem(fixture, MediaKind.Folder));
            await pending;
            Check(canceled.Thumbnail is null && image.Source is null, "取消的旧请求不能污染新卡片");
            Check(MediaCache.GetThumbnail(canceled.Version, 512) is null, "取消的加载不能写入缓存");
            MediaCache.Clear();
            Check(MediaCache.GetThumbnail(version, 128) is null && MediaCache.AspectRatios.Count == 0, "关闭清理应释放缓存");
            foreach (var videoName in new[] { "video.mp4", "video.mov" })
            {
                var videoPath = Path.GetFullPath(Path.Combine(workspace, "work/validation/media", videoName));
                var videoFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(videoPath);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var frame = await VideoThumbnail.GetFirstFrameAsync(videoFile, 160, timeout.Token);
                var decoded = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(frame);
                checkFrame(decoded.PixelWidth, decoded.PixelHeight);
                using var canceledFrame = new CancellationTokenSource();
                canceledFrame.Cancel();
                try
                {
                    using var unexpected = await VideoThumbnail.GetFirstFrameAsync(videoFile, 160, canceledFrame.Token);
                    Check(false, "取消的取帧请求不应成功");
                }
                catch (OperationCanceledException) { Check(true, "首帧取消"); }
            }
            void checkFrame(uint width, uint height) => Check(width > 0 && height > 0 && Math.Max(width, height) <= 160,
                "首帧必须可解码且保持缩略图尺寸限制");
            var brokenVideo = Path.Combine(fixture, "broken.mp4");
            File.WriteAllText(brokenVideo, "not a video");
            var brokenItem = new MediaItem(brokenVideo, MediaKind.Video, FileVersion.Read(brokenVideo));
            tile.Bind(brokenItem);
            tile.SetPresentation(true, false, false);
            Check(((TextBlock)tile.FindName("LoadingText")).Visibility == Visibility.Visible && badge.Visibility == Visibility.Visible,
                "视频加载期间必须显示状态和角标");
            await brokenItem.EnsureThumbnailAsync(128);
            Check(brokenItem.Thumbnail is null && brokenItem.ThumbnailFailed && failure.Visibility == Visibility.Visible,
                "无效视频取帧失败必须有提示");
            Check(((TextBlock)tile.FindName("LoadingText")).Visibility == Visibility.Collapsed && badge.Visibility == Visibility.Visible,
                "失败时应结束加载提示并保留播放入口");
            await new GalleryHarness().RunAsync(Check);
            File.WriteAllText(report, $"PASS: {checks} real WinUI tile/cache checks. Navigation and visual layout require manual validation.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(report, $"FAIL: {ex}");
            Environment.ExitCode = 1;
        }
        Exit();
    }
}
