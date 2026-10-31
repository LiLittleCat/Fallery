using Windows.Media.Editing;
using Windows.Storage;
using Windows.Graphics.Imaging;

namespace Fallery.Services;

public static class VideoThumbnail
{
    // 独立限制视频解码并发，避免坏视频和高分辨率视频同时占用过多解码器。
    private static readonly SemaphoreSlim DecodeSlots = new(2);

    /// <summary>按原始分辨率取指定时间的画面，用于截图；不依赖播放器的帧服务器。</summary>
    public static async Task<ImageStream> GetFrameAsync(StorageFile file, TimeSpan time, CancellationToken ct)
    {
        await DecodeSlots.WaitAsync(ct);
        var composition = new MediaComposition();
        try
        {
            var clip = await MediaClip.CreateFromFileAsync(file).AsTask(ct);
            ct.ThrowIfCancellationRequested();
            composition.Clips.Add(clip);
            var properties = clip.GetVideoEncodingProperties();
            return await composition.GetThumbnailAsync(time, (int)properties.Width, (int)properties.Height,
                VideoFramePrecision.NearestFrame).AsTask(ct);
        }
        finally
        {
            composition.Clips.Clear();
            DecodeSlots.Release();
        }
    }

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
