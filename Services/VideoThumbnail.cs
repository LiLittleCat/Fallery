using Windows.Media.Editing;
using Windows.Storage;
using Windows.Graphics.Imaging;

namespace Fallery.Services;

public static class VideoThumbnail
{
    // 独立限制视频解码并发，避免坏视频和高分辨率视频同时占用过多解码器。
    private static readonly SemaphoreSlim DecodeSlots = new(2);

    public static async Task<ImageStream> GetFirstFrameAsync(StorageFile file, uint size, CancellationToken ct)
    {
        await DecodeSlots.WaitAsync(ct);
        var composition = new MediaComposition();
        try
        {
            var clip = await MediaClip.CreateFromFileAsync(file).AsTask(ct);
            ct.ThrowIfCancellationRequested();
            composition.Clips.Add(clip);
            var properties = clip.GetVideoEncodingProperties();
            bool landscape = properties.Width >= properties.Height;
            return await composition.GetThumbnailAsync(TimeSpan.Zero,
                landscape ? (int)size : 0, landscape ? 0 : (int)size,
                VideoFramePrecision.NearestFrame).AsTask(ct);
        }
        finally
        {
            composition.Clips.Clear();
            DecodeSlots.Release();
        }
    }
}
