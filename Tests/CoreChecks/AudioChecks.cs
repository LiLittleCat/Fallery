using Fallery.Models;
using Fallery.Services;

internal static class AudioChecks
{
    internal static async Task RunAsync(string fixture, Action<bool, string> check)
    {
        string album = Path.Combine(fixture, "音乐 专辑");
        string wave = Path.Combine(album, "曲目 01.WAV");
        AudioFixture.Write(wave);
        foreach (string extension in new[] { ".MP3", ".wav", ".flac", ".m4a", ".m4b", ".aac", ".wma", ".aiff", ".ogg", ".opus" })
            check(FolderScanner.GetMediaKind("sample" + extension) == MediaKind.Audio
                && FolderScanner.SupportedExtensions.Contains(extension.ToLowerInvariant()), "音频扩展名识别及打开方式列表应一致且忽略大小写");
        check((int)MediaKind.Folder == 2 && (int)MediaKind.Audio == 3 && (int)MediaFilter.Videos == 2,
            "追加音频不能改变旧索引和设置中的枚举含义");
        check(FolderScanner.MatchesFilter(MediaKind.Audio, MediaFilter.All) && FolderScanner.MatchesFilter(MediaKind.Audio, MediaFilter.Audio)
            && !FolderScanner.MatchesFilter(MediaKind.Audio, MediaFilter.Photos) && !FolderScanner.MatchesFilter(MediaKind.Audio, MediaFilter.Videos), "音频筛选独立于图片和视频");
        check(FolderScanner.Scan(album).Single().Kind == MediaKind.Audio, "只有音频的目录也能扫描到媒体");
        check(FolderScanner.ScanEntries(fixture).Single().Kind == MediaKind.Folder
            && FolderScanner.ContainsMediaRecursive(fixture, filter: MediaFilter.Audio), "只有音频的后代目录不能被无媒体过滤隐藏");
        check(FolderScanner.ScanEntries(fixture, filter: MediaFilter.Photos).Count == 0, "仅图片筛选隐藏纯音频目录");
        check(await MediaProbe.TryGetAspectRatioAsync(wave, MediaKind.Audio) == 1, "音频布局使用方形，不探测视频尺寸");
        var details = await MediaDetailsReader.ReadAsync(wave, MediaKind.Audio, default);
        check(details.Resolution == "" && details.Groups.Any(group => group.Group == "音频" && group.Rows.Any(row => row.Label == "时长")),
            "真实 WAV 可读到时长且不伪造分辨率");
        var index = new MediaSearchIndex(Path.Combine(fixture, "audio.idx"));
        await index.BuildAsync([fixture], default);
        foreach (string query in new[] { "type:audio", "type:music", "type:音频", "ext:wav" })
        {
            var result = await index.SearchAsync(query, false, default);
            check(result.Total == 1 && result.Items[0].Path == wave, "音频索引支持类型、中文及扩展名查询");
        }
        var reloaded = new MediaSearchIndex(Path.Combine(fixture, "audio.idx"));
        check(await reloaded.LoadAsync(default) && (await reloaded.SearchAsync("type:audio", false, default)).Total == 1,
            "音频索引跨实例持久化");
        var settings = new AppSettingsStore(Path.Combine(fixture, "settings.json"));
        check(await settings.SaveAsync(new AppSettings { Filter = MediaFilter.Audio }) && (await settings.LoadAsync()).Filter == MediaFilter.Audio,
            "音频筛选可跨启动保存");
    }
}
