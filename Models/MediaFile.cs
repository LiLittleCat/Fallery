namespace Fallery.Models;

public enum MediaKind { Image, Video, Folder }
public enum MediaSortField { Name, Modified, Size, Taken, Created }
public enum MediaFilter { All, Photos, Videos }

// 扫描结果不携带 UI 对象，可以在后台生成和测试。
public sealed record MediaFile(string Path, MediaKind Kind)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public FileVersion? Version { get; init; }
    public long? FolderModifiedTicks { get; init; }
    public long? CreatedTicks { get; init; }
    public long? TakenTicks { get; init; }
    public MediaFilter PreviewFilter { get; init; }
    public long? ModifiedTicks => Version?.ModifiedTicks ?? FolderModifiedTicks;
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
