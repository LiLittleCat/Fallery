using Fallery.Services;
namespace Fallery.Models;

public enum MediaKind { Image, Video, Folder, Audio }
public enum MediaSortField { Name, Modified, Size, Taken, Created }
public enum MediaFilter { All, Photos, Videos, Audio }

// 扫描结果不携带 UI 对象，可以在后台生成和测试。
public sealed record MediaFile(string Path, MediaKind Kind)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public FileVersion? Version { get; init; }
    public long? FolderModifiedTicks { get; init; }
    public long? CreatedTicks { get; init; }
    public long? TakenTicks { get; init; }
    public MediaFilter PreviewFilter { get; init; }
    public bool PreviewHidden { get; init; }
    public long? ModifiedTicks => Version?.ModifiedTicks ?? FolderModifiedTicks;
    public string TypeText => Kind == MediaKind.Folder ? AppText.Get("Folder") : $"{System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant()} {Kind switch { MediaKind.Video => AppText.Get("Videos"), MediaKind.Audio => AppText.Get("Audio"), _ => AppText.Get("Photos") }}";
    public string SizeText => Kind == MediaKind.Folder || Version is null ? "—" : Version.Value.Length switch
    {
        >= 1L << 30 => $"{Version.Value.Length / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{Version.Value.Length / (double)(1L << 20):0.##} MB",
        >= 1L << 10 => $"{Version.Value.Length / 1024d:0.##} KB",
        var bytes => $"{bytes} B"
    };
    public string CreatedText => DateText(CreatedTicks);
    public string ModifiedText => DateText(ModifiedTicks);
    private static string DateText(long? ticks) => ticks is > 0 && ticks <= DateTime.MaxValue.Ticks
        ? new DateTime(ticks.Value, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
}

// 路径和文件版本一起标识缓存，避免同名文件覆盖后继续显示旧图。
public readonly record struct FileVersion(string Path, long Length, long ModifiedTicks)
{
    public static FileVersion? Read(string path) => FromInfo(new FileInfo(path));

    public static FileVersion? FromInfo(FileInfo info)
    {
        try
        {
            return info.Exists ? new(info.FullName.ToUpperInvariant(), info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
