using Fallery.Models;
using Fallery.Services;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class PhotoMetadataChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var empty = new Dictionary<string, object>();
        check(PhotoMetadata.CameraRows(empty).Count == 0 && PhotoMetadata.LocationRows(empty, null, null).Count == 0,
            "无 EXIF 时不能产生空卡片或默认坐标");
        var values = new Dictionary<string, object>
        {
            ["System.Photo.CameraManufacturer"] = "Canon",
            ["System.Photo.CameraModel"] = "Canon EOS R5",
            ["System.Photo.LensModel"] = " RF 24-70mm F2.8 L IS USM\0",
            ["System.Photo.ISOSpeed"] = (uint)400,
            ["System.Photo.ExposureTime"] = 0.8d,
            ["System.Photo.ExposureBias"] = 0d,
            ["System.Photo.FocalLength"] = double.NaN,
            ["System.Photo.WhiteBalanceText"] = " ",
        };
        var camera = PhotoMetadata.CameraRows(values).ToDictionary(row => row.Label, row => row.Value);
        check(camera["相机"] == "Canon EOS R5" && camera["镜头"] == "RF 24-70mm F2.8 L IS USM", "相机品牌不重复，镜头去除 EXIF 尾零");
        check(camera["感光度"] == "ISO 400" && camera["快门"].StartsWith("0.8") && camera["曝光补偿"] == "0 EV",
            "EXIF 数值兼容不同整数类型，保留小数快门和零曝光补偿");
        check(!camera.ContainsKey("焦距") && !camera.ContainsKey("白平衡"), "忽略无效数值和空白字段");
        var gps = PhotoMetadata.LocationRows(new Dictionary<string, object>
        {
            ["System.GPS.Altitude"] = 12.5d, ["System.GPS.AltitudeRef"] = (byte)1,
        }, -33.5, -70.25).ToDictionary(row => row.Label, row => row.Value);
        check(gps["纬度"] == "33.5° 南纬" && gps["经度"] == "70.25° 西经" && gps["海拔"] == "-12.5 米", "南纬、西经和海平面以下海拔符号正确");
        check(PhotoMetadata.LocationRows(empty, 0, 0).Count == 2, "零经纬度不能当作缺失值");
        check(PhotoMetadata.LocationRows(empty, 91, double.NaN).Count == 0, "越界/非有限坐标不展示");

        Directory.CreateDirectory(directory);
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);
        var file = await folder.CreateFileAsync("with-exif.jpg", CreationCollisionOption.ReplaceExisting);
        using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 4, 4, 96, 96, new byte[64]);
            await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet
            {
                ["/app1/ifd/exif/{ushort=42035}"] = new BitmapTypedValue("Example", PropertyType.String),
                ["/app1/ifd/exif/{ushort=42036}"] = new BitmapTypedValue("Example 50mm F1.8", PropertyType.String),
            });
            await encoder.FlushAsync();
        }
        await file.Properties.SavePropertiesAsync(new Dictionary<string, object>
        {
            ["System.Photo.CameraManufacturer"] = "Example",
            ["System.Photo.CameraModel"] = "Camera 1",
            ["System.Photo.FNumberNumerator"] = (uint)18,
            ["System.Photo.FNumberDenominator"] = (uint)10,
            ["System.Photo.ExposureTimeNumerator"] = (uint)1,
            ["System.Photo.ExposureTimeDenominator"] = (uint)125,
            ["System.Photo.ISOSpeed"] = (ushort)200,
            ["System.GPS.LatitudeNumerator"] = new uint[] { 33, 30, 0 },
            ["System.GPS.LatitudeDenominator"] = new uint[] { 1, 1, 1 },
            ["System.GPS.LatitudeRef"] = "S",
            ["System.GPS.LongitudeNumerator"] = new uint[] { 70, 15, 0 },
            ["System.GPS.LongitudeDenominator"] = new uint[] { 1, 1, 1 },
            ["System.GPS.LongitudeRef"] = "W",
        });
        var original = await File.ReadAllBytesAsync(file.Path);
        var details = await MediaDetailsReader.ReadAsync(file.Path, MediaKind.Image, CancellationToken.None);
        var actual = details.Groups.SelectMany(group => group.Rows).ToDictionary(row => row.Label, row => row.Value);
        check(actual.GetValueOrDefault("相机") == "Example Camera 1" && actual.GetValueOrDefault("镜头") == "Example 50mm F1.8",
            "生产读取器必须读取真实 JPEG 相机和原始 EXIF 镜头标签");
        check(actual.GetValueOrDefault("光圈") == "f/1.8" && actual.GetValueOrDefault("快门") == "1/125 秒"
            && actual.GetValueOrDefault("感光度") == "ISO 200", "生产读取器必须读取真实 JPEG 曝光参数");
        check(actual.GetValueOrDefault("纬度") == "33.5° 南纬" && actual.GetValueOrDefault("经度") == "70.25° 西经",
            "生产读取器必须转换真实 JPEG GPS 度分秒与方向");
        var afterRead = await File.ReadAllBytesAsync(file.Path);
        check(original.SequenceEqual(afterRead), "读取 EXIF 不得修改图片文件");

        var plain = await folder.CreateFileAsync("without-exif.png", CreationCollisionOption.ReplaceExisting);
        using (var stream = await plain.OpenAsync(FileAccessMode.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 4, 4, 96, 96, new byte[64]);
            await encoder.FlushAsync();
        }
        var plainDetails = await MediaDetailsReader.ReadAsync(plain.Path, MediaKind.Image, CancellationToken.None);
        check(plainDetails.Width == 4 && !plainDetails.Groups.Any(group => group.Group is "拍摄" or "地理位置"),
            "没有 EXIF 的真实 PNG 保留尺寸且不出现空拍摄/地理卡片");
        // 自生成的纯色 WebP，不依赖用户样本；伪装扩展名也必须按实际解码器判断。
        byte[] webp = Convert.FromBase64String("UklGRh4AAABXRUJQVlA4TBEAAAAvH8AFAAdQvLaUtv+BiOh/AAA=");
        foreach (string name in new[] { "without-exif.webp", "webp-named-jpeg.jpg" })
        {
            string webpPath = Path.Combine(directory, name);
            await File.WriteAllBytesAsync(webpPath, webp);
            var webpFile = await StorageFile.GetFileFromPathAsync(webpPath);
            var metadata = new Dictionary<string, object>();
            await PhotoMetadata.FillLensAsync(webpFile, metadata, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            check(metadata.Count == 0, "WebP 不得使用 JPEG/TIFF 原始镜头标签查询（含扩展名伪装）");
        }
        var webpDetails = await MediaDetailsReader.ReadAsync(Path.Combine(directory, "without-exif.webp"), MediaKind.Image, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        check(webpDetails.Width == 32 && webpDetails.Height == 24
            && !webpDetails.Groups.Any(group => group.Group is "拍摄" or "地理位置"),
            "WebP 详情保留尺寸，缺失 EXIF 不生成空分组");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool observed = false;
        try { await MediaDetailsReader.ReadAsync(file.Path, MediaKind.Image, canceled.Token); }
        catch (OperationCanceledException) { observed = true; }
        check(observed, "切换图片取消后，EXIF 读取不得继续提交结果");
    }
}
