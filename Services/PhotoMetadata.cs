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
            rows.Add(("拍摄时间", date.LocalDateTime.ToString("yyyy年M月d日 HH:mm:ss", CultureInfo.CurrentCulture)));
        Add("相机", Combine(Text(values, "System.Photo.CameraManufacturer"), Text(values, "System.Photo.CameraModel")));
        Add("镜头", Combine(Text(values, "System.Photo.LensManufacturer"), Text(values, "System.Photo.LensModel")));
        Positive("光圈", "System.Photo.FNumber", value => $"f/{value:0.##}");
        Positive("快门", "System.Photo.ExposureTime", value => value < 0.5 ? $"1/{1 / value:0.##} 秒" : $"{value:0.###} 秒");
        Positive("感光度", "System.Photo.ISOSpeed", value => $"ISO {value:0}");
        Positive("焦距", "System.Photo.FocalLength", value => $"{value:0.##} mm");
        Positive("等效焦距", "System.Photo.FocalLengthInFilm", value => $"{value:0.##} mm（35 mm）");
        if (Number(values, "System.Photo.ExposureBias") is { } bias)
            rows.Add(("曝光补偿", $"{bias:+0.##;-0.##;0} EV"));
        Add("曝光程序", Text(values, "System.Photo.ExposureProgramText"));
        Add("测光模式", Text(values, "System.Photo.MeteringModeText"));
        Add("白平衡", Text(values, "System.Photo.WhiteBalanceText"));
        Add("闪光灯", Text(values, "System.Photo.FlashText"));
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
            rows.Add(("纬度", Coordinate(lat, "北纬", "南纬")));
        if (longitude is { } lon && double.IsFinite(lon) && Math.Abs(lon) <= 180)
            rows.Add(("经度", Coordinate(lon, "东经", "西经")));
        if (Number(values, "System.GPS.Altitude") is >= 0 and var altitude
            && Number(values, "System.GPS.AltitudeRef") is { } reference && reference is 0 or 1)
            rows.Add(("海拔", $"{(reference == 1 ? -altitude : altitude):0.##} 米"));
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
            string root = decoder.DecoderInformation.CodecId == BitmapDecoder.JpegDecoderId ? "/app1/ifd/exif" : "/ifd/exif";
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
