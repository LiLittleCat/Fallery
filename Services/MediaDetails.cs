using System.Globalization;
using Fallery.Models;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Fallery.Services;

/// <summary>查看器底栏和详细信息面板显示的内容；分组按 Windows 照片的信息面板排列。</summary>
public sealed record MediaDetails(string Format, uint Width, uint Height, long Length, IReadOnlyList<(string Group, IReadOnlyList<(string Label, string Value)> Rows)> Groups)
{
    public string Resolution => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "";
    public string Size => MediaDetailsReader.FormatSize(Length);
}

public static class MediaDetailsReader
{
    private static readonly string[] PhotoKeys =
    [
        "System.Photo.DateTaken", "System.Photo.CameraManufacturer", "System.Photo.CameraModel", "System.Photo.FNumber",
        "System.Photo.ExposureTime", "System.Photo.ISOSpeed", "System.Photo.FocalLength", "System.Image.HorizontalResolution",
        "System.Image.BitDepth", "System.Photo.LensManufacturer", "System.Photo.LensModel",
        "System.Photo.FocalLengthInFilm", "System.Photo.ExposureBias", "System.Photo.ExposureProgramText",
        "System.Photo.MeteringModeText", "System.Photo.WhiteBalanceText", "System.Photo.FlashText",
        "System.GPS.Altitude", "System.GPS.AltitudeRef",
    ];
    private static readonly string[] VideoKeys = ["System.Video.FrameRate", "System.Video.TotalBitrate", "System.Media.DateEncoded", "System.Audio.SampleRate"];

    /// <summary>不打开文件内容的基础信息，打开查看器时立即显示。</summary>
    public static MediaDetails Basic(string path, MediaKind kind)
    {
        var info = new FileInfo(path);
        long length = 0;
        DateTime? modified = null, created = null;
        try
        {
            if (info.Exists)
            {
                length = info.Length;
                modified = info.LastWriteTime;
                created = info.CreationTime;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var file = new List<(string, string)> { ("文件名", info.Name), ("格式", FormatOf(path)), ("大小", FormatSize(length)) };
        if (modified is { } m) file.Add(("修改日期", m.ToString("yyyy年M月d日 HH:mm", CultureInfo.CurrentCulture)));
        if (created is { } c) file.Add(("创建日期", c.ToString("yyyy年M月d日 HH:mm", CultureInfo.CurrentCulture)));
        return new(FormatOf(path), 0, 0, length, [("文件", file), ("位置", [("文件夹", info.DirectoryName ?? "")])]);
    }

    /// <summary>读取分辨率、拍摄参数或视频时长等，失败时保留基础信息。</summary>
    public static async Task<MediaDetails> ReadAsync(string path, MediaKind kind, CancellationToken ct)
    {
        var basic = Basic(path, kind);
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct);
            if (kind == MediaKind.Audio)
            {
                var music = await file.Properties.GetMusicPropertiesAsync().AsTask(ct);
                var extra = await file.Properties.RetrievePropertiesAsync(new[] { "System.Audio.SampleRate", "System.Audio.ChannelCount" }).AsTask(ct);
                var rows = new List<(string, string)>();
                if (!string.IsNullOrWhiteSpace(music.Title)) rows.Add(("标题", music.Title));
                if (!string.IsNullOrWhiteSpace(music.Artist)) rows.Add(("艺术家", music.Artist));
                if (!string.IsNullOrWhiteSpace(music.Album)) rows.Add(("专辑", music.Album));
                if (music.Duration > TimeSpan.Zero) rows.Add(("时长", music.Duration.TotalHours >= 1 ? music.Duration.ToString(@"h\:mm\:ss") : music.Duration.ToString(@"m\:ss")));
                if (music.Bitrate > 0) rows.Add(("码率", $"{music.Bitrate / 1000.0:N0} kbps"));
                if (music.TrackNumber > 0) rows.Add(("曲目", music.TrackNumber.ToString()));
                if (music.Year > 0) rows.Add(("年份", music.Year.ToString()));
                if (extra.TryGetValue("System.Audio.SampleRate", out var sample) && sample is uint hz && hz > 0) rows.Add(("采样率", $"{hz / 1000.0:0.#} kHz"));
                if (extra.TryGetValue("System.Audio.ChannelCount", out var channels) && channels is uint count && count > 0) rows.Add(("声道", count.ToString()));
                return basic with { Groups = [basic.Groups[0], ("音频", rows), basic.Groups[1]] };
            }
            if (kind == MediaKind.Video)
            {
                var video = await file.Properties.GetVideoPropertiesAsync().AsTask(ct);
                var extra = await file.Properties.RetrievePropertiesAsync(VideoKeys).AsTask(ct);
                bool rotated = video.Orientation is VideoOrientation.Rotate90 or VideoOrientation.Rotate270;
                uint width = rotated ? video.Height : video.Width, height = rotated ? video.Width : video.Height;
                var rows = new List<(string, string)>();
                if (width > 0) rows.Add(("分辨率", $"{width} × {height}"));
                if (video.Duration > TimeSpan.Zero) rows.Add(("时长", video.Duration.TotalHours >= 1 ? video.Duration.ToString(@"h\:mm\:ss") : video.Duration.ToString(@"m\:ss")));
                if (extra.TryGetValue("System.Video.FrameRate", out var fps) && fps is uint frameRate && frameRate > 0) rows.Add(("帧率", $"{frameRate / 1000.0:0.##} 帧/秒"));
                uint bitrate = extra.TryGetValue("System.Video.TotalBitrate", out var total) && total is uint t && t > 0 ? t : video.Bitrate;
                if (bitrate > 0) rows.Add(("码率", $"{bitrate / 1000.0:N0} kbps"));
                if (extra.TryGetValue("System.Audio.SampleRate", out var sample) && sample is uint sampleRate && sampleRate > 0) rows.Add(("音频采样率", $"{sampleRate / 1000.0:0.#} kHz"));
                if (extra.TryGetValue("System.Media.DateEncoded", out var encoded) && encoded is DateTimeOffset date && date.Year > 1601)
                    rows.Add(("拍摄日期", date.LocalDateTime.ToString("yyyy年M月d日 HH:mm", CultureInfo.CurrentCulture)));
                return basic with { Width = width, Height = height, Groups = [basic.Groups[0], ("视频", rows), basic.Groups[1]] };
            }
            else
            {
                var image = await file.Properties.GetImagePropertiesAsync().AsTask(ct);
                var extra = await file.Properties.RetrievePropertiesAsync(PhotoKeys).AsTask(ct);
                bool rotated = image.Orientation is PhotoOrientation.Rotate90 or PhotoOrientation.Rotate270 or PhotoOrientation.Transpose or PhotoOrientation.Transverse;
                uint width = rotated ? image.Height : image.Width, height = rotated ? image.Width : image.Height;
                var rows = new List<(string, string)>();
                if (width > 0) rows.Add(("分辨率", $"{width} × {height}"));
                if (width > 0) rows.Add(("像素", $"{width * (double)height / 1_000_000:0.#} 百万"));
                var dpi = extra.TryGetValue("System.Image.HorizontalResolution", out var d) && d is double dpiValue && dpiValue > 0 ? $"{dpiValue:0} dpi" : null;
                var depth = extra.TryGetValue("System.Image.BitDepth", out var b) && b is uint bits && bits > 0 ? $"{bits} 位" : null;
                if (dpi is not null || depth is not null) rows.Add(("dpi / 位深", string.Join("  ", new[] { dpi, depth }.OfType<string>())));
                await PhotoMetadata.FillLensAsync(file, extra, ct);
                var camera = PhotoMetadata.CameraRows(extra);
                var location = PhotoMetadata.LocationRows(extra, image.Latitude, image.Longitude);
                var groups = new List<(string, IReadOnlyList<(string, string)>)> { basic.Groups[0], ("图像", rows) };
                if (camera.Count > 0) groups.Add(("拍摄", camera));
                if (location.Count > 0) groups.Add(("地理位置", location));
                groups.Add(basic.Groups[1]);
                return basic with { Width = width, Height = height, Groups = groups };
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return basic; }
    }

    public static string FormatOf(string path) => Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} 字节",
    };
}
