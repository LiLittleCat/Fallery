using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Fallery.Models;

namespace Fallery.Services;

public sealed record MotionPhotoClip(FileVersion Version, long Offset, long Length);

public static class MotionPhoto
{
    // 新版目录规范：https://developer.android.com/media/platform/motion-photo-format
    // 保留旧版 MicroVideoOffset 兼容；不能只凭标记判定，编辑器可能已移除附加视频。
    private const long MaxVideoBytes = 256L * 1024 * 1024;
    private const string Camera = "http://ns.google.com/photos/1.0/camera/";
    private const string Container = "http://ns.google.com/photos/1.0/container/";
    private const string Item = "http://ns.google.com/photos/1.0/container/item/";

    public static Task<MotionPhotoClip?> FindAsync(string path, CancellationToken ct) => Task.Run(() => Find(path, ct), ct);

    private static MotionPhotoClip? Find(string path, CancellationToken ct)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is not (".jpg" or ".jpeg")) return null;
        try
        {
            var version = FileVersion.Read(path);
            if (version is null) return null;
            using var stream = File.OpenRead(path);
            if (stream.ReadByte() != 0xff || stream.ReadByte() != 0xd8) return null;
            // 只读取 JPEG 标记中的 XMP，不扫描原图像素或整段视频。
            while (stream.Position < Math.Min(stream.Length, 4 * 1024 * 1024))
            {
                ct.ThrowIfCancellationRequested();
                if (stream.ReadByte() != 0xff) return null;
                int marker;
                do { marker = stream.ReadByte(); } while (marker == 0xff);
                if (marker is -1 or 0xda or 0xd9) return null;
                if (marker == 0x01 || marker is >= 0xd0 and <= 0xd7) continue;
                int high = stream.ReadByte(), low = stream.ReadByte();
                int length = (high << 8 | low) - 2;
                if (high < 0 || low < 0 || length < 0 || length > stream.Length - stream.Position) return null;
                if (marker != 0xe1) { stream.Position += length; continue; }
                var bytes = new byte[length];
                stream.ReadExactly(bytes);
                byte[] prefix = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();
                if (!bytes.AsSpan().StartsWith(prefix)) continue;
                string xml = Encoding.UTF8.GetString(bytes, prefix.Length, bytes.Length - prefix.Length).TrimEnd('\0');
                using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 128 * 1024 });
                var doc = XDocument.Load(reader);
                string? Value(string name) => doc.Descendants().Attributes(XName.Get(name, Camera)).FirstOrDefault()?.Value
                    ?? doc.Descendants(XName.Get(name, Camera)).FirstOrDefault()?.Value;
                long videoLength;
                if (Value("MotionPhoto") is { } modern)
                {
                    if (modern != "1" || Value("MotionPhotoVersion") != "1") return null;
                    var entries = doc.Descendants(XName.Get("Item", Container)).ToArray();
                    var video = entries.LastOrDefault();
                    if (video is null || (string?)video.Attribute(XName.Get("Semantic", Item)) != "MotionPhoto"
                        || (string?)video.Attribute(XName.Get("Mime", Item)) is not ("video/mp4" or "video/quicktime")
                        || !long.TryParse((string?)video.Attribute(XName.Get("Length", Item)), NumberStyles.None,
                            CultureInfo.InvariantCulture, out videoLength)) return null;
                }
                else
                {
                    if (Value("MicroVideo") != "1" || !long.TryParse(Value("MicroVideoOffset"), NumberStyles.None,
                        CultureInfo.InvariantCulture, out videoLength)) return null;
                }
                if (videoLength < 24 || videoLength > MaxVideoBytes || videoLength > stream.Length - stream.Position) return null;
                long offset = stream.Length - videoLength;
                if (!IsVideoContainer(stream, offset, videoLength, ct) || FileVersion.Read(path) != version) return null;
                return new(version.Value, offset, videoLength);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException) { }
        return null;
    }

    private static bool IsVideoContainer(Stream stream, long offset, long length, CancellationToken ct)
    {
        stream.Position = offset;
        long end = offset + length;
        bool movie = false, data = false, first = true;
        Span<byte> header = stackalloc byte[16];
        for (int count = 0; count < 10000 && stream.Position < end; count++)
        {
            ct.ThrowIfCancellationRequested();
            long start = stream.Position;
            if (end - start < 8) return false;
            stream.ReadExactly(header[..8]);
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
            string type = Encoding.ASCII.GetString(header[4..8]);
            int headerSize = 8;
            if (size == 1)
            {
                if (end - start < 16) return false;
                stream.ReadExactly(header[8..]);
                size = BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
                headerSize = 16;
            }
            if (size == 0) size = (ulong)(end - start);
            if (size < (ulong)headerSize || size > (ulong)(end - start)) return false;
            if (first && (type != "ftyp" || size < 16)) return false;
            first = false;
            movie |= type == "moov";
            data |= type == "mdat";
            stream.Position = start + (long)size;
        }
        return stream.Position == end && movie && data;
    }

    public static async Task<string> ExtractAsync(MotionPhotoClip clip, string directory, CancellationToken ct)
    {
        string output = Path.Combine(directory, $"motion-{Guid.NewGuid():N}.mp4");
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(directory);
                // 处理上次异常退出留下的自有临时视频，不动其它临时文件。
                foreach (var old in new DirectoryInfo(directory).EnumerateFiles("motion-*.mp4"))
                {
                    ct.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow - old.LastWriteTimeUtc < TimeSpan.FromDays(1)) continue;
                    try { old.Delete(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }, ct).ConfigureAwait(false);
            if (await Task.Run(() => FileVersion.Read(clip.Version.Path), ct) != clip.Version)
                throw new IOException(AppText.Get("ThePhotoHasChangedReopenItToPlayThe"));
            await using var input = new FileStream(clip.Version.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (clip.Offset < 0 || clip.Length < 24 || clip.Length > MaxVideoBytes || clip.Offset > input.Length - clip.Length)
                throw new IOException(AppText.Get("InvalidMotionVideoRange"));
            input.Position = clip.Offset;
            await using (var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536];
                long remaining = clip.Length;
                while (remaining > 0)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                    if (read == 0) throw new EndOfStreamException();
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    remaining -= read;
                }
            }
            ct.ThrowIfCancellationRequested();
            return output;
        }
        catch { await DeleteTemporaryAsync(output); throw; }
    }

    public static Task DeleteTemporaryAsync(string path) => Task.Run(async () =>
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { File.Delete(path); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Task.Delay(200); }
        }
    });
}
