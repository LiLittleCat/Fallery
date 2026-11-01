using Fallery.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Fallery.Services;

public static class MediaProbe
{
    /// <summary>只读图片文件头拿原始像素尺寸；系统缩略图会把小图放大，不能用缩略图尺寸判断。</summary>
    public static async Task<(uint Width, uint Height)?> TryGetImageSizeAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct);
            using var stream = await file.OpenReadAsync().AsTask(ct);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
            return (decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }

    /// <summary>只读文件头拿宽高（已按 EXIF/视频旋转校正），失败返回 null。可在后台线程调用。</summary>
    public static async Task<double?> TryGetAspectRatioAsync(string path, MediaKind kind, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (kind == MediaKind.Folder) return null;
        if (kind == MediaKind.Audio) return 1;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct);
            if (kind == MediaKind.Image)
            {
                using var stream = await file.OpenReadAsync().AsTask(ct);
                var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
                if (decoder.OrientedPixelWidth == 0 || decoder.OrientedPixelHeight == 0) return null;
                return (double)decoder.OrientedPixelWidth / decoder.OrientedPixelHeight;
            }

            var props = await file.Properties.GetVideoPropertiesAsync().AsTask(ct);
            if (props.Width == 0 || props.Height == 0) return null;
            bool rotated = props.Orientation is VideoOrientation.Rotate90 or VideoOrientation.Rotate270;
            return rotated ? (double)props.Height / props.Width : (double)props.Width / props.Height;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return null;
        }
    }
}
