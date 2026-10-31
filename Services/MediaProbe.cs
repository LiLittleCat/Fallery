using Fallery.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Fallery.Services;

public static class MediaProbe
{
    /// <summary>只读文件头拿宽高（已按 EXIF/视频旋转校正），失败返回 null。可在后台线程调用。</summary>
    public static async Task<double?> TryGetAspectRatioAsync(MediaItem item)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.Path);
            if (item.Kind == MediaKind.Image)
            {
                using var stream = await file.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(stream);
                if (decoder.OrientedPixelWidth == 0 || decoder.OrientedPixelHeight == 0) return null;
                return (double)decoder.OrientedPixelWidth / decoder.OrientedPixelHeight;
            }

            var props = await file.Properties.GetVideoPropertiesAsync();
            if (props.Width == 0 || props.Height == 0) return null;
            bool rotated = props.Orientation is VideoOrientation.Rotate90 or VideoOrientation.Rotate270;
            return rotated ? (double)props.Height / props.Width : (double)props.Width / props.Height;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
