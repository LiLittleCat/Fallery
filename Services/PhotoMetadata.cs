using System.Globalization;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Fallery.Services;

/// <summary>只读 EXIF 展示；缺失/无效字段不生成行，零坐标和零曝光补偿仍是有效数据。</summary>
internal static class PhotoMetadata
{
    internal static List<(string Label, string Value)> CameraRows(IDictionary<string, object> values)
    {
        var rows = new List<(string, string)>();
        if (values.TryGetValue("System.Photo.DateTaken", out var taken) && taken is DateTimeOffset date && date.Year > 1601)
            rows.Add((AppText.Get("DateTaken50b635"), date.LocalDateTime.ToString(AppText.Get("MMMDYyyyHHMmSs"), AppText.Culture)));
        Add(AppText.Get("Cameradacf07"), Combine(Text(values, "System.Photo.CameraManufacturer"), Text(values, "System.Photo.CameraModel")));
        Add(AppText.Get("Lens"), Combine(Text(values, "System.Photo.LensManufacturer"), Text(values, "System.Photo.LensModel")));
        Positive(AppText.Get("Aperture"), "System.Photo.FNumber", value => $"f/{value:0.##}");
        Positive(AppText.Get("ShutterSpeed"), "System.Photo.ExposureTime", value => value < 0.5 ? AppText.Format("S", 1 / value) : AppText.Format("Sbc41ce", value));
        Positive(AppText.Get("ISO"), "System.Photo.ISOSpeed", value => $"ISO {value:0}");
        Positive(AppText.Get("FocalLength"), "System.Photo.FocalLength", value => $"{value:0.##} mm");
        Positive(AppText.Get("MmEquivalent"), "System.Photo.FocalLengthInFilm", value => $"{value:0.##} mm（35 mm）");
        if (Number(values, "System.Photo.ExposureBias") is { } bias)
            rows.Add((AppText.Get("ExposureCompensation"), $"{bias:+0.##;-0.##;0} EV"));
        Add(AppText.Get("ExposureProgram"), Text(values, "System.Photo.ExposureProgramText"));
        Add(AppText.Get("MeteringMode"), Text(values, "System.Photo.MeteringModeText"));
        Add(AppText.Get("WhiteBalance"), Text(values, "System.Photo.WhiteBalanceText"));
        Add(AppText.Get("Flash"), Text(values, "System.Photo.FlashText"));
        return rows;

        void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) rows.Add((label, value)); }
        void Positive(string label, string key, Func<double, string> format)
        {
            if (Number(values, key) is > 0 and var value) rows.Add((label, format(value)));
        }
    }

    internal static List<(string Label, string Value)> LocationRows(IDictionary<string, object> values, double? latitude, double? longitude)
    {
        var rows = new List<(string, string)>();
        if (latitude is { } lat && double.IsFinite(lat) && Math.Abs(lat) <= 90)
            rows.Add((AppText.Get("Latitude"), Coordinate(lat, AppText.Get("N"), AppText.Get("S4e13ae"))));
        if (longitude is { } lon && double.IsFinite(lon) && Math.Abs(lon) <= 180)
            rows.Add((AppText.Get("Longitude"), Coordinate(lon, AppText.Get("E"), AppText.Get("W"))));
        if (Number(values, "System.GPS.Altitude") is >= 0 and var altitude
            && Number(values, "System.GPS.AltitudeRef") is { } reference && reference is 0 or 1)
            rows.Add((AppText.Get("Altitude"), AppText.Format("M", (reference == 1 ? -altitude : altitude))));
        return rows;
    }

    private static string Coordinate(double value, string positive, string negative) =>
        Math.Abs(value).ToString("0.######", CultureInfo.InvariantCulture) + "°" + (value == 0 ? "" : " " + (value < 0 ? negative : positive));

    // Windows 的 LensModel 属性主要映射 XMP；相机写入的 EXIF 2.3 镜头标签需要直接查询。
    internal static async Task FillLensAsync(StorageFile file, IDictionary<string, object> values, CancellationToken ct)
    {
        if (Text(values, "System.Photo.LensManufacturer") is not null && Text(values, "System.Photo.LensModel") is not null) return;
        try
        {
            using var stream = await file.OpenReadAsync().AsTask(ct);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
            // TIFF 查询不能交给其他格式的扩展解码器：部分 WebP 会在原生元数据接口中卡死。
            var codec = decoder.DecoderInformation.CodecId;
            string? root = codec == BitmapDecoder.JpegDecoderId ? "/app1/ifd/exif"
                : codec == BitmapDecoder.TiffDecoderId ? "/ifd/exif" : null;
            if (root is null) return;
            foreach (var (key, tag) in new[] { ("System.Photo.LensManufacturer", 42035), ("System.Photo.LensModel", 42036) })
            {
                if (Text(values, key) is not null) continue;
                ct.ThrowIfCancellationRequested();
                try
                {
                    string query = $"{root}/{{ushort={tag}}}";
                    var properties = await decoder.BitmapProperties.GetPropertiesAsync([query]).AsTask(ct);
                    if (properties.TryGetValue(query, out var property) && property.Value is string raw
                        && Clean(raw) is { } text)
                    {
                        values[key] = text;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { /* 不支持的标签不能丢失其他已读属性。 */ }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* 无元数据或不支持的格式保留系统已读取的信息。 */ }
    }

    private static string? Text(IDictionary<string, object> values, string key) =>
        values.TryGetValue(key, out var value) && value is string text ? Clean(text) : null;

    private static string? Clean(string text)
    {
        string cleaned = text.Trim('\0', ' ', '\r', '\n', '\t');
        return cleaned.Length > 0 ? cleaned : null;
    }

    private static string? Combine(string? make, string? model) => model is null ? make
        : make is null || model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? model : $"{make} {model}";

    private static double? Number(IDictionary<string, object> values, string key)
    {
        if (!values.TryGetValue(key, out var value)) return null;
        double number = value switch
        {
            byte v => v, ushort v => v, uint v => v, ulong v => v,
            sbyte v => v, short v => v, int v => v, long v => v,
            float v => v, double v => v, _ => double.NaN,
        };
        return double.IsFinite(number) ? number : null;
    }
}
