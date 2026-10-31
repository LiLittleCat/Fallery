using Fallery.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Fallery.Services;

public static class MediaCache
{
    // 这是缓存引用的解码像素预算，不包含可见控件、GPU 和运行时的内存。
    private static readonly BoundedCache<FileVersion, CachedThumbnail> Thumbnails = new(128L * 1024 * 1024, 512);
    public static BoundedCache<FileVersion, double> AspectRatios { get; } = new(20_000, 20_000);
    private sealed record CachedThumbnail(BitmapImage Image, uint RequestedSize);

    // BitmapImage 的取存与使用都在 UI 线程；后台只访问纯数值宽高比缓存。
    public static BitmapImage? GetThumbnail(FileVersion? version, uint size)
        => version is { } key && Thumbnails.TryGet(key, out var entry) && entry.RequestedSize >= size
            ? entry.Image : null;

    public static void StoreThumbnail(FileVersion? version, uint size, BitmapImage image)
    {
        if (version is not { } key) return;
        if (Thumbnails.TryGet(key, out var existing) && existing.RequestedSize >= size) return;
        // 解码时限制长边，按正方形计费可保守估计像素内存，兼容极端宽高比。
        Thumbnails.Set(key, new(image, size), (long)size * size * 4);
    }

    public static void Clear()
    {
        Thumbnails.Clear();
        AspectRatios.Clear();
        MediaDates.Clear();
    }
}
