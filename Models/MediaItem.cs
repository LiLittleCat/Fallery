using CommunityToolkit.Mvvm.ComponentModel;
using Fallery.Services;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Fallery.Models;

public sealed partial class MediaItem : ObservableObject
{
    private static readonly SemaphoreSlim ThumbnailSlots = new(6);
    private CancellationTokenSource? _thumbnailCts;
    private uint _thumbnailSize;
    private uint _pendingSize;

    public MediaItem(string path, MediaKind kind, FileVersion? version = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        Kind = kind;
        Version = version;
        File = new(Path, kind) { Version = version };
        if (version is { } key && MediaCache.AspectRatios.TryGet(key, out var ratio))
        {
            AspectRatio = ratio;
            HasAspectRatio = true;
        }
    }

    public MediaItem(MediaFile file) : this(file.Path, file.Kind, file.Version) => File = file;

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public MediaKind Kind { get; }
    public FileVersion? Version { get; }
    public MediaFile File { get; }
    public bool HasAspectRatio { get; set; }
    public bool IsVideo => Kind == MediaKind.Video;
    public bool IsFolder => Kind == MediaKind.Folder;

    /// <summary>宽 / 高。探测完成前按 1:1 占位，由 MasonryLayout 读取。</summary>
    public double AspectRatio { get; set; } = 1.0;

    [ObservableProperty]
    public partial BitmapImage? Thumbnail { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<BitmapImage> FolderThumbnails { get; set; } = [];

    [ObservableProperty]
    public partial bool ThumbnailFailed { get; set; }

    /// <summary>在 UI 线程调用。元素被回收后再完成的加载会被丢弃。</summary>
    public const int MinCoverPixels = 128;
    public const uint MaxThumbnailPixels = 512;

    public async Task EnsureThumbnailAsync(uint size)
    {
        // 在查缓存前统一限幅，避免大尺寸请求反复升级同一张缩略图、挤掉最近目录。
        size = Math.Clamp(size, 1u, MaxThumbnailPixels);
        if ((Thumbnail is not null && _thumbnailSize >= size)
            || (_thumbnailCts is not null && _pendingSize >= size)) return;
        _thumbnailCts?.Cancel();
        if (!IsFolder && MediaCache.GetThumbnail(Version, size) is { } cached)
        {
            _thumbnailCts = null;
            Thumbnail = cached;
            _thumbnailSize = size;
            ThumbnailFailed = false;
            return;
        }
        using var cts = new CancellationTokenSource();
        _thumbnailCts = cts;
        _pendingSize = size;
        var ct = cts.Token;
        bool acquired = false;
        try
        {
            await ThumbnailSlots.WaitAsync(ct);
            acquired = true;
            IEnumerable<MediaFile> candidates = IsFolder
                ? FolderScanner.EnumerateFolderPreviews(Path, ct, File.PreviewFilter, File.PreviewHidden).Take(8)
                : [new(Path, Kind) { Version = Version }];
            using var candidateIterator = candidates.GetEnumerator();
            var covers = new List<BitmapImage>(3);
            // 逐个取得候选，三张可用封面就停止，不为凑齐八个候选继续扫描后代。
            while (await Task.Run(candidateIterator.MoveNext, ct))
            {
                var candidate = candidateIterator.Current;
                try
                {
                    var version = candidate.Version ?? await Task.Run(() => FileVersion.Read(candidate.Path), ct);
                    ct.ThrowIfCancellationRequested();
                    // 图标之类的小图放大成封面会发糊，宁可显示文件夹图标；读不出尺寸的仍交给系统缩略图。
                    if (IsFolder && candidate.Kind == MediaKind.Image
                        && await Task.Run(() => MediaProbe.TryGetImageSizeAsync(candidate.Path, ct), ct) is { } pixels
                        && pixels.Width < MinCoverPixels && pixels.Height < MinCoverPixels) continue;
                    var bitmap = MediaCache.GetThumbnail(version, size);
                    if (bitmap is null)
                    {
                        bitmap = await LoadThumbnailBitmapAsync(candidate.Path, candidate.Kind, version, size, ct);
                        ct.ThrowIfCancellationRequested();
                        var afterLoad = await Task.Run(() => FileVersion.Read(candidate.Path), ct);
                        ct.ThrowIfCancellationRequested();
                        // 加载中被替换的文件不能以旧版本写进缓存。
                        if (afterLoad != version) continue;
                        if (bitmap is not null) MediaCache.StoreThumbnail(version, size, bitmap);
                    }
                    ct.ThrowIfCancellationRequested();
                    if (bitmap is null) continue;
                    if (IsFolder)
                    {
                        covers.Add(bitmap);
                        FolderThumbnails = covers.ToArray();
                    }
                    Thumbnail = IsFolder ? covers[0] : bitmap;
                    _thumbnailSize = size;
                    ThumbnailFailed = false;
                    if (!IsFolder || covers.Count == 3) return;
                }
                catch (OperationCanceledException) when (IsFolder && !ct.IsCancellationRequested) { }
                catch (OperationCanceledException) { throw; }
                catch (Exception) when (IsFolder) { }
            }
            ct.ThrowIfCancellationRequested();
            ThumbnailFailed = !IsFolder;
        }
        catch (OperationCanceledException)
        {
            if (!ct.IsCancellationRequested) ThumbnailFailed = !IsFolder;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Thumbnail load failed for {0}: {1}", Path, ex);
            if (!ct.IsCancellationRequested) ThumbnailFailed = !IsFolder;
        }
        finally
        {
            if (acquired) ThumbnailSlots.Release();
            if (ReferenceEquals(_thumbnailCts, cts)) _thumbnailCts = null;
        }
    }

    private static async Task<BitmapImage?> LoadThumbnailBitmapAsync(string path, MediaKind kind, FileVersion? version, uint size, CancellationToken ct)
    {
        var disk = MediaCache.Disk;
        int generation = disk.Generation;
        if (version is { } key && await disk.ReadAsync(key, size, ct) is { } data)
        {
            try
            {
                using var memory = new MemoryStream(data);
                using var cachedStream = memory.AsRandomAccessStream();
                return await DecodeThumbnailAsync(cachedStream, size, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { await disk.RemoveAsync(key); }
        }
        // 卡片回收会取消请求；系统取图长时间无响应时也应结束等待并显示失败状态。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        ct = timeout.Token;
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct);
        try
        {
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size,
                ThumbnailOptions.ResizeThumbnail).AsTask(ct);
            ct.ThrowIfCancellationRequested();
            if (thumb is not null && thumb.Size > 0 && thumb.Type == ThumbnailType.Image)
                return await DecodeAndStoreAsync(thumb);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) when (kind == MediaKind.Video) { }
        ct.ThrowIfCancellationRequested();
        if (kind != MediaKind.Video) return null;
        using var frame = await VideoThumbnail.GetFirstFrameAsync(file, size, ct);
        return await DecodeAndStoreAsync(frame);

        async Task<BitmapImage> DecodeAndStoreAsync(IRandomAccessStream stream)
        {
            var bitmap = await DecodeThumbnailAsync(stream, size, ct);
            if (version is { } current && stream.Size is > 0 and <= ThumbnailDiskCache.MaxEntryBytes)
            {
                stream.Seek(0);
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                uint length = (uint)stream.Size;
                if (await reader.LoadAsync(length).AsTask(ct) == length)
                {
                    var encoded = new byte[length];
                    reader.ReadBytes(encoded);
                    await disk.StoreAsync(current, size, encoded, generation, ct);
                }
            }
            return bitmap;
        }
    }

    private static async Task<BitmapImage> DecodeThumbnailAsync(IRandomAccessStream thumb, uint size, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // 原生解码完成前保留输入流；回收只取消发布，不能提前释放仍被 WinUI 使用的流。
        var decoder = await BitmapDecoder.CreateAsync(thumb);
        ct.ThrowIfCancellationRequested();
        var bitmap = new BitmapImage { AutoPlay = false };
        // 约束长边，避免极窄长图按列宽放大后占用大量解码内存。
        if (decoder.PixelWidth >= decoder.PixelHeight)
            bitmap.DecodePixelWidth = (int)Math.Min(size, decoder.PixelWidth);
        else bitmap.DecodePixelHeight = (int)Math.Min(size, decoder.PixelHeight);
        thumb.Seek(0);
        await bitmap.SetSourceAsync(thumb);
        ct.ThrowIfCancellationRequested();
        return bitmap;
    }

    public void ReleaseThumbnail()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts = null;
        Thumbnail = null;
        FolderThumbnails = [];
        _thumbnailSize = 0;
        ThumbnailFailed = false;
    }
}
