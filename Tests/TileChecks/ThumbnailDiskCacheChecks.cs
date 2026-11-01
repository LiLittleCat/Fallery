using Fallery.Models;
using Fallery.Services;

namespace Fallery.TileChecks;

internal static class ThumbnailDiskCacheChecks
{
    public static async Task RunAsync(string fixture, Action<bool, string> check)
    {
        string root = Path.Combine(fixture, "disk-policy");
        var cache = new ThumbnailDiskCache(root, byteLimit: 80, countLimit: 2);
        var versions = Enumerable.Range(0, 3).Select(i =>
        {
            string path = Path.Combine(fixture, $"disk-source-{i}.bin");
            File.WriteAllText(path, "source");
            return FileVersion.Read(path)!.Value;
        }).ToArray();
        byte[] bytes = Enumerable.Range(1, 24).Select(i => (byte)i).ToArray();
        await cache.StoreAsync(versions[0], 256, bytes, cache.Generation, default);
        await cache.StoreAsync(versions[1], 256, bytes, cache.Generation, default);
        check((await cache.ReadAsync(versions[0], 128, default))!.SequenceEqual(bytes), "磁盘缓存应复用较大尺寸");
        check(await cache.ReadAsync(versions[0], 512, default) is null, "磁盘小图不得冒充高清缓存");
        await cache.StoreAsync(versions[2], 256, bytes, cache.Generation, default);
        check(await cache.ReadAsync(versions[1], 128, default) is null
            && await cache.ReadAsync(versions[0], 128, default) is not null, "磁盘缓存超额应淘汰最久未用项");
        check(new DirectoryInfo(root).EnumerateFiles().Sum(f => f.Length) <= 80, "磁盘缓存不能超过字节预算");
        var smallerBudget = new ThumbnailDiskCache(root, byteLimit: 40, countLimit: 10);
        await smallerBudget.ReadAsync(versions[2], 128, default);
        check(new DirectoryInfo(root).EnumerateFiles().Sum(f => f.Length) <= 40,
            "重启后的磁盘缓存也必须按字节预算淘汰，而非只限制数量");
        var modified = versions[0] with { ModifiedTicks = versions[0].ModifiedTicks + 1 };
        await cache.RemoveManyAsync([versions[0]]);
        check(await cache.ReadAsync(versions[0], 128, default) is null
            && await cache.ReadAsync(versions[2], 128, default) is not null, "删除只清理对应版本的磁盘缩略图，保留其他文件缓存");
        check(await cache.ReadAsync(modified, 128, default) is null, "文件版本变化必须使磁盘缓存失效");
        await cache.StoreAsync(modified, 256, bytes, cache.Generation, default);
        check(await cache.ReadAsync(modified, 128, default) is null, "加载中变化的文件不得以错误版本写入磁盘");
        int oldGeneration = cache.Generation;
        await cache.ClearAsync();
        await cache.StoreAsync(versions[0], 256, bytes, oldGeneration, default);
        check(!Directory.EnumerateFiles(root).Any(), "清空后旧任务不能把缓存写回来");
        await cache.StoreAsync(versions[0], 256, bytes, cache.Generation, default);
        string cached = Directory.GetFiles(root, "*.thumb").Single();
        File.WriteAllBytes(cached, [1, 2, 3]);
        check(await cache.ReadAsync(versions[0], 128, default) is null && !File.Exists(cached), "损坏缓存头应被移除");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await cache.StoreAsync(versions[0], 256, bytes, cache.Generation, canceled.Token); check(false, "取消写入应抛出取消"); }
        catch (OperationCanceledException) { check(!Directory.EnumerateFiles(root).Any(), "取消写入不能留下半成品"); }
        string blocked = Path.Combine(fixture, "unavailable-cache");
        File.WriteAllText(blocked, "not a directory");
        var unavailable = new ThumbnailDiskCache(blocked);
        await unavailable.StoreAsync(versions[0], 128, bytes, unavailable.Generation, default);
        check(await unavailable.ReadAsync(versions[0], 128, default) is null, "缓存目录不可用必须允许回退正常加载");
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        MediaCache.StoreThumbnail(versions[0], 128, bitmap);
        MediaCache.StoreThumbnail(versions[1], 128, bitmap);
        MediaCache.AspectRatios.Set(versions[0], 1.5);
        await MediaCache.RemoveDeletedAsync([versions[0].Path], [versions[0]]);
        check(MediaCache.GetThumbnail(versions[0], 128) is null && MediaCache.GetThumbnail(versions[1], 128) is not null,
            "删除后的内存缩略图必须按路径移除，不能清空无关文件缓存");
        check(!MediaCache.AspectRatios.TryGet(versions[0], out _), "删除后同时移除宽高比缓存");
    }
}
