using Fallery.Models;
using Windows.Storage;

namespace Fallery.Services;

public static class MediaDates
{
    private sealed record DateValue(long? Ticks);
    private static readonly BoundedCache<FileVersion, DateValue> Dates = new(20_000, 20_000);
    private static readonly SemaphoreSlim Slots = new(4);

    public static void Clear() => Dates.Clear();

    public static async Task<List<MediaFile>> ReadAsync(IEnumerable<MediaFile> files, CancellationToken ct)
    {
        var result = files.ToArray();
        await Parallel.ForEachAsync(Enumerable.Range(0, result.Length),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (index, token) =>
        {
            var item = result[index];
            if (item.Kind == MediaKind.Folder) return;
            if (item.Version is { } key && Dates.TryGet(key, out var cached))
            {
                result[index] = item with { TakenTicks = cached.Ticks };
                return;
            }
            await Slots.WaitAsync(token);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var file = await StorageFile.GetFileFromPathAsync(item.Path).AsTask(timeout.Token);
                string property = item.Kind == MediaKind.Image ? "System.Photo.DateTaken" : "System.Media.DateEncoded";
                var properties = await file.Properties.RetrievePropertiesAsync(new[] { property }).AsTask(timeout.Token);
                token.ThrowIfCancellationRequested();
                long? ticks = properties.TryGetValue(property, out var value) && value is DateTimeOffset date && date.Year > 1601
                    ? date.UtcTicks : null;
                // 覆盖后的文件不能继续使用旧版本的拍摄日期；失败/超时不缓存为“无日期”。
                if (item.Version is not { } version || FileVersion.Read(item.Path) != version) return;
                Dates.Set(version, new(ticks));
                result[index] = item with { TakenTicks = ticks };
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { }
            finally { Slots.Release(); }
        });
        return result.ToList();
    }
}
