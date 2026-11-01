using System.Buffers.Binary;
using System.Text;
using Fallery.Services;

internal static class MotionPhotoChecks
{
    internal static async Task RunAsync(string fixture, Action<bool, string> check)
    {
        Directory.CreateDirectory(fixture);
        byte[] Box(string type, byte[] payload)
        {
            var bytes = new byte[payload.Length + 8];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
            Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
            payload.CopyTo(bytes, 8);
            return bytes;
        }
        byte[] video = [.. Box("ftyp", "isom0000"u8.ToArray()), .. Box("moov", []), .. Box("mdat", [1, 2, 3, 4])];
        string Save(string name, string camera, string items = "", byte[]? payload = null)
        {
            string xml = $"<x:xmpmeta xmlns:x='adobe:ns:meta/'><r:RDF xmlns:r='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><r:Description xmlns:c='http://ns.google.com/photos/1.0/camera/' xmlns:g='http://ns.google.com/photos/1.0/container/' xmlns:i='http://ns.google.com/photos/1.0/container/item/' {camera}>{items}</r:Description></r:RDF></x:xmpmeta>";
            byte[] data = [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. Encoding.UTF8.GetBytes(xml)];
            string path = Path.Combine(fixture, name + ".jpg");
            File.WriteAllBytes(path, [0xff, 0xd8, 0xff, 0xe1, (byte)((data.Length + 2) >> 8), (byte)(data.Length + 2),
                .. data, 0xff, 0xd9, .. payload ?? video]);
            return path;
        }
        string legacy = Save("legacy", $"c:MicroVideo='1' c:MicroVideoOffset='{video.Length}'");
        var clip = await MotionPhoto.FindAsync(legacy, default);
        check(clip is not null && clip.Length == video.Length, "旧版 MicroVideoOffset 必须正确识别");
        string extracted = await MotionPhoto.ExtractAsync(clip!, Path.Combine(fixture, "temp"), default);
        check(File.ReadAllBytes(extracted).SequenceEqual(video), "只提取视频范围，不携带 JPEG 及其它附加图像");
        await MotionPhoto.DeleteTemporaryAsync(extracted);
        check(!File.Exists(extracted), "播放临时文件必须可以清理");
        string directory = $"<g:Directory><r:Seq><r:li><g:Item i:Semantic='Primary' i:Mime='image/jpeg'/></r:li><r:li><g:Item i:Semantic='MotionPhoto' i:Mime='video/mp4' i:Length='{video.Length}'/></r:li></r:Seq></g:Directory>";
        check(await MotionPhoto.FindAsync(Save("modern", "c:MotionPhoto='1' c:MotionPhotoVersion='1'", directory), default) is not null,
            "新版容器目录格式及不同 XMP 前缀必须正确识别");
        check(await MotionPhoto.FindAsync(Save("disabled", $"c:MotionPhoto='0' c:MicroVideo='1' c:MicroVideoOffset='{video.Length}'", directory), default) is null,
            "明确关闭 MotionPhoto 时不能回退旧标记误判");
        check(await MotionPhoto.FindAsync(Save("oversize", "c:MicroVideo='1' c:MicroVideoOffset='999999999999'"), default) is null,
            "越界视频长度不能显示实况按钮");
        check(await MotionPhoto.FindAsync(Save("stripped", $"c:MicroVideo='1' c:MicroVideoOffset='{video.Length}'", payload: []), default) is null,
            "照片编辑后残留标记但视频已删除时不能误判");
        check(await MotionPhoto.FindAsync(Save("broken", $"c:MicroVideo='1' c:MicroVideoOffset='{video.Length}'", payload: new byte[video.Length]), default) is null,
            "不是有效 MP4 容器的附加内容不能误判");
        check(await MotionPhoto.FindAsync(Save("plain", ""), default) is null, "普通 JPG 不显示实况");
        File.AppendAllText(legacy, "changed");
        try { await MotionPhoto.ExtractAsync(clip!, Path.Combine(fixture, "temp"), default); check(false, "变化后的原文件必须拒绝旧片段"); }
        catch (IOException) { check(true, "变化后的原文件必须拒绝旧片段"); }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await MotionPhoto.FindAsync(legacy, canceled.Token); check(false, "切图必须取消旧查找"); }
        catch (OperationCanceledException) { check(true, "取消查找"); }
        string? sample = Environment.GetEnvironmentVariable("FALLERY_MOTION_SAMPLE");
        if (!string.IsNullOrEmpty(sample))
        {
            var actual = await MotionPhoto.FindAsync(sample, default);
            check(actual is not null, "用户原片必须识别为实况照片");
            string movie = await MotionPhoto.ExtractAsync(actual!, Path.Combine(fixture, "temp"), default);
            var storage = await Windows.Storage.StorageFile.GetFileFromPathAsync(movie);
            var metadata = await storage.Properties.GetVideoPropertiesAsync();
            check(metadata.Width > 0 && metadata.Height > 0 && metadata.Duration.TotalSeconds > 0,
                "用户原片的视频必须能由 Windows 读取尺寸和时长");
            Console.WriteLine($"Motion sample: {metadata.Width}x{metadata.Height}, {metadata.Duration.TotalSeconds:F2}s, {actual!.Length} bytes");
            await MotionPhoto.DeleteTemporaryAsync(movie);
        }
    }
}
