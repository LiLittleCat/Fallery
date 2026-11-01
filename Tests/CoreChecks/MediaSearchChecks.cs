using Fallery.Services;
using Fallery.Models;

internal static class MediaSearchChecks
{
    internal static async Task RunAsync(string fixture, Action<bool, string> check)
    {
        var rootAttributes = FileAttributes.Directory | FileAttributes.System | FileAttributes.Hidden;
        check(MediaSearchIndex.TryGetDirectoryVisibility(@"C:\", rootAttributes, false, out bool rootHidden) && !rootHidden,
            "系统/隐藏卷根必须可遍历，且不向普通后代传播隐藏属性");
        check(MediaSearchIndex.TryGetDirectoryVisibility(@"c:/", rootAttributes, false, out rootHidden) && !rootHidden,
            "卷根识别应支持不同路径分隔符");
        check(!MediaSearchIndex.TryGetDirectoryVisibility(@"C:\protected", rootAttributes, false, out _),
            "系统子目录不能享受卷根的属性豁免");
        check(!MediaSearchIndex.TryGetDirectoryVisibility(@"C:\", rootAttributes | FileAttributes.ReparsePoint, false, out _),
            "卷根也不能跟随重解析点");
        check(MediaSearchIndex.TryGetDirectoryVisibility(@"C:\photos", FileAttributes.Directory, true, out bool inherited) && inherited,
            "普通目录必须继续继承隐藏父目录状态");
        string first = Directory.CreateDirectory(Path.Combine(fixture, "盘一")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(fixture, "盘二")).FullName;
        string album = Directory.CreateDirectory(Path.Combine(first, "旅行 相册")).FullName;
        await File.WriteAllTextAsync(Path.Combine(first, "cat.jpg"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(first, "cathedral.jpg"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(album, "beach sunset.JPG"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(album, "beach sunrise.png"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(second, "beach sunset.mp4"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(second, "notes.txt"), "fixture");
        string hidden = Directory.CreateDirectory(Path.Combine(first, "secret")).FullName;
        File.SetAttributes(hidden, FileAttributes.Directory | FileAttributes.Hidden);
        await File.WriteAllTextAsync(Path.Combine(hidden, "hidden.jpg"), "fixture");
        string excluded = Directory.CreateDirectory(Path.Combine(first, "node_modules")).FullName;
        await File.WriteAllTextAsync(Path.Combine(excluded, "excluded.jpg"), "fixture");
        string storage = Path.Combine(fixture, "index", "media.idx");
        var index = new MediaSearchIndex(storage);
        await index.BuildAsync([first, second], default);
        check(!index.Status.Building && !index.Status.Limited && File.Exists(storage), "索引必须完成并持久化");
        async Task<SearchMatches> Find(string query, bool showHidden = false, int limit = 300)
            => await index.SearchAsync(query, showHidden, default, limit);
        check((await Find("BEACH")).Total == 3, "跨根目录搜索必须忽略大小写");
        check((await Find("beach sunset")).Total == 2 && (await Find("\"beach sunset\" ext:jpg")).Total == 1,
            "关键词组合和带空格短语必须正确匹配");
        check((await Find("*.jpg")).Total == 3 && (await Find("beach ext:jpg;png")).Total == 2,
            "通配符与多个扩展名应正确筛选");
        check((await Find("type:video")).Items.Single().Kind == MediaKind.Video
            && (await Find("type:folder 相册")).Total == 1, "媒体和文件夹类型筛选应独立生效");
        check((await Find("path:旅行 type:photo")).Total == 2
            && (await Find("path:\"旅行 相册\" sunset")).Total == 1, "路径及带空格路径条件必须有效");
        check((await Find("hidden")).Total == 0 && (await Find("hidden", true)).Total == 1,
            "隐藏父目录中的普通文件也必须遵守隐藏项开关");
        check((await Find("excluded", true)).Total == 0 && (await Find("notes")).Total == 0,
            "开发缓存目录与不支持的普通文件不进入媒体索引");
        check((await Find("cat")).Items[0].Name == "cat.jpg", "名称前缀匹配应排在后续字母名称之前");
        var limitedResults = await Find("beach", limit: 1);
        check(limitedResults.Items.Count == 1 && limitedResults.Total == 3, "界面结果数量受限时仍应报告总数");
        var restored = new MediaSearchIndex(storage);
        check(await restored.LoadAsync(default) && (await restored.SearchAsync("beach", false, default)).Total == 3,
            "重启后应直接从磁盘索引恢复查询");
        File.Delete(Path.Combine(second, "beach sunset.mp4"));
        File.Move(Path.Combine(first, "cat.jpg"), Path.Combine(first, "renamed.jpg"));
        await index.BuildAsync([first, second], default);
        check((await Find("type:video")).Total == 0 && (await Find("renamed")).Total == 1 && (await Find("cat.jpg")).Total == 0,
            "刷新必须同步删除和改名，不能保留旧路径");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await index.SearchAsync("beach", false, canceled.Token); check(false, "旧查询必须支持取消"); }
        catch (OperationCanceledException) { check(true, "取消查询"); }
        var capped = new MediaSearchIndex(Path.Combine(fixture, "small.idx"), entryLimit: 2);
        await capped.BuildAsync([first, second], default);
        check(capped.Status.Count == 2 && capped.Status.Limited, "达到索引上限必须明确标记不完整");
        string earlyDrive = Directory.CreateDirectory(Path.Combine(fixture, "early-drive")).FullName;
        string laterDrive = Directory.CreateDirectory(Path.Combine(fixture, "later-drive")).FullName;
        string deepFolder = Directory.CreateDirectory(Path.Combine(earlyDrive, "deep")).FullName;
        await File.WriteAllTextAsync(Path.Combine(deepFolder, "deep.jpg"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(laterDrive, "later.jpg"), "fixture");
        await capped.BuildAsync([earlyDrive, laterDrive], default);
        check((await capped.SearchAsync("later.jpg", false, default)).Total == 1,
            "有限预算内应先遍历各磁盘当前层，再进入单个磁盘的深层目录");
        await File.WriteAllTextAsync(storage, "broken index");
        check(!await new MediaSearchIndex(storage).LoadAsync(default), "损坏索引必须安全回退重建");
        var inaccessible = new MediaSearchIndex(Path.Combine(fixture, "missing.idx"));
        await inaccessible.BuildAsync([Path.Combine(fixture, "missing")], default);
        check(inaccessible.Status.Skipped == 1 && inaccessible.Status.Limited && !File.Exists(Path.Combine(fixture, "missing.idx")),
            "无法读取任何根目录不能伪装成完整搜索成功或保存空索引");
        await restored.BuildAsync([], default);
        check(restored.Status.Limited && (await restored.SearchAsync("beach", false, default)).Total == 3,
            "没有可用磁盘时应保留已有索引并报告未完成");
        string protectedRoot = Directory.CreateDirectory(Path.Combine(fixture, "protected-root")).FullName;
        string deletionRoot = Directory.CreateDirectory(Path.Combine(fixture, "delete-index")).FullName;
        string deleteAlbum = Directory.CreateDirectory(Path.Combine(deletionRoot, "album")).FullName;
        string keepAlbum = Directory.CreateDirectory(Path.Combine(deletionRoot, "album-other")).FullName;
        await File.WriteAllTextAsync(Path.Combine(deleteAlbum, "remove.jpg"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(keepAlbum, "keep.jpg"), "fixture");
        string deletionStorage = Path.Combine(fixture, "delete.idx");
        var deletionIndex = new MediaSearchIndex(deletionStorage);
        await deletionIndex.BuildAsync([deletionRoot], default);
        await deletionIndex.RemovePathsAsync([deleteAlbum], default);
        check((await deletionIndex.SearchAsync("remove", false, default)).Total == 0
            && (await deletionIndex.SearchAsync("keep", false, default)).Total == 1,
            "删除目录应移除后代索引，但不能移除相同前缀的兄弟目录");
        var deletionReloaded = new MediaSearchIndex(deletionStorage);
        check(await deletionReloaded.LoadAsync(default) && (await deletionReloaded.SearchAsync("remove", false, default)).Total == 0,
            "删除索引应持久化，重启不能恢复已删除路径");
        var rebuilding = deletionIndex.BuildAsync([deletionRoot], default);
        var removing = deletionIndex.RemovePathsAsync([Path.Combine(keepAlbum, "keep.jpg")], default);
        await Task.WhenAll(rebuilding, removing);
        check((await deletionIndex.SearchAsync("keep", false, default)).Total == 0,
            "并发构建完成后删除仍须生效，不能被旧扫描结果覆盖");
        try
        {
            File.SetAttributes(protectedRoot, rootAttributes);
            await File.WriteAllTextAsync(Path.Combine(protectedRoot, "protected.jpg"), "fixture");
            await inaccessible.BuildAsync([protectedRoot], default);
            check(inaccessible.Status.Count == 0 && inaccessible.Status.Limited,
                "显式传入的普通系统目录也必须被跳过，不能当成卷根");
        }
        finally { File.SetAttributes(protectedRoot, FileAttributes.Directory); }
        var localRoots = MediaSearchIndex.LocalRoots();
        check(localRoots.Length > 0, "真实本地磁盘应可发现");
        // 只检查测试样本所在卷（确定非空）；其他已就绪磁盘可能是空盘。
        foreach (string root in localRoots.Where(root => string.Equals(root, Path.GetPathRoot(fixture), StringComparison.OrdinalIgnoreCase)))
        {
            var rootIndex = new MediaSearchIndex(Path.Combine(fixture, "root.idx"), entryLimit: 1);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await rootIndex.BuildAsync([root], timeout.Token);
            check(rootIndex.Status.Count == 1, "真实磁盘根应能枚举至少一个普通目录或媒体：" + root);
        }
    }

    internal static async Task CheckLocalSampleAsync(string fixture, string sample)
    {
        if (!File.Exists(sample)) throw new FileNotFoundException("验证原片不存在", sample);
        var index = new MediaSearchIndex(Path.Combine(fixture, "local-search.idx"));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long lastReport = 0;
        index.Progress += status =>
        {
            if (!status.Building || clock.ElapsedMilliseconds - lastReport > 10000)
            { Console.WriteLine(status.Message); lastReport = clock.ElapsedMilliseconds; }
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        string name = Path.GetFileNameWithoutExtension(sample);
        string query = name[..Math.Min(4, name.Length)];
        var build = index.BuildAsync(MediaSearchIndex.LocalRoots(), timeout.Token);
        // 从真实卷根开始；找到原片即可停止，避免回归诊断反复扫描整机。
        while (!build.IsCompleted)
        {
            await Task.WhenAny(build, Task.Delay(1000));
            var partial = await index.SearchAsync(query, false, default);
            if (partial.Items.Any(entry => string.Equals(entry.Path, sample, StringComparison.OrdinalIgnoreCase)))
            { timeout.Cancel(); break; }
        }
        try { await build; }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        var result = await index.SearchAsync(query, false, default);
        if (!result.Items.Any(entry => string.Equals(entry.Path, sample, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("真实全盘索引未找到验证原片");
        Console.WriteLine($"真实卷根有界扫描搜索通过：{sample}；{result.Total} 个匹配；耗时 {clock.Elapsed.TotalSeconds:F1}s（找到即停止，不代表完整索引耗时）");
    }
}
