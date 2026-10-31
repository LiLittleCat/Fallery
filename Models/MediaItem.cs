using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Fallery.Models;

public enum MediaKind { Image, Video }

public sealed partial class MediaItem : ObservableObject
{
    private int _thumbnailVersion;

    public MediaItem(string path, MediaKind kind)
    {
        Path = path;
        Kind = kind;
    }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public MediaKind Kind { get; }
    public bool IsVideo => Kind == MediaKind.Video;

    /// <summary>宽 / 高。探测完成前按 1:1 占位，由 MasonryLayout 读取。</summary>
    public double AspectRatio { get; set; } = 1.0;

    [ObservableProperty]
    public partial BitmapImage? Thumbnail { get; set; }

    /// <summary>在 UI 线程调用。元素被回收后再完成的加载会被丢弃。</summary>
    public async Task EnsureThumbnailAsync(uint size)
    {
        if (Thumbnail is not null) return;
        int version = ++_thumbnailVersion;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.ResizeThumbnail);
            if (thumb is null || version != _thumbnailVersion) return;

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumb);
            if (version != _thumbnailVersion) return;
            Thumbnail = bitmap;
        }
        catch (Exception)
        {
            // 损坏的文件、没有权限或系统缺少解码器：保持占位色块
        }
    }

    public void ReleaseThumbnail()
    {
        _thumbnailVersion++;
        Thumbnail = null;
    }
}
