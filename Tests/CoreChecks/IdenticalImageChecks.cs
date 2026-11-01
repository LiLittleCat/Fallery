using Fallery.Services;

internal static class IdenticalImageChecks
{
    internal static async Task RunAsync(string fixture, Action<bool, string> check)
    {
        string root = Directory.CreateDirectory(Path.Combine(fixture, "scan")).FullName;
        string child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
        string sample = Path.Combine(root, "logo.png");
        string renamed = Path.Combine(child, "renamed.gif");
        string different = Path.Combine(child, "logo.png");
        string twin = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "twin")).FullName, "LOGO.PNG");
        string larger = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "larger")).FullName, "logo.png");
        await File.WriteAllTextAsync(sample, "same bytes");
        await File.WriteAllTextAsync(renamed, "same bytes");
        await File.WriteAllTextAsync(Path.Combine(child, "logo.gif"), "same bytes");
        await File.WriteAllTextAsync(different, "diff bytes");
        await File.WriteAllTextAsync(twin, "same bytes");
        await File.WriteAllTextAsync(larger, "different size and content");
        await File.WriteAllTextAsync(Path.Combine(child, "not-image.txt"), "same bytes");
        string excluded = Directory.CreateDirectory(Path.Combine(root, "node_modules")).FullName;
        await File.WriteAllTextAsync(Path.Combine(excluded, "logo.png"), "same bytes");
        string hidden = Directory.CreateDirectory(Path.Combine(root, "hidden")).FullName;
        File.SetAttributes(hidden, FileAttributes.Directory | FileAttributes.Hidden);
        await File.WriteAllTextAsync(Path.Combine(hidden, "logo.png"), "same bytes");
        var found = await IdenticalImages.ScanAsync(root, [sample], false, null, default);
        check(found.Matches.Count == 2 && found.Matches.Any(match => match.Path == sample)
            && found.Matches.Any(match => match.Path == twin), "默认必须名称、大小和内容都相同，名称不区分大小写并包含样本");
        check(found.Matches.All(match => match.Path != different && match.Path != larger && match.Path != renamed)
            && found.Skipped == 0, "默认模式排除同名不同内容、同名不同大小和异名相同内容");
        var nameOnly = await IdenticalImages.ScanAsync(root, [sample], false, null, default, mode: IdenticalImageMatchMode.NameOnly);
        check(nameOnly.Matches.Count == 4 && nameOnly.Matches.Any(match => match.Path == different)
            && nameOnly.Matches.Any(match => match.Path == larger)
            && nameOnly.Matches.All(match => match.Path != renamed && match.Mode == IdenticalImageMatchMode.NameOnly),
            "仅名称模式应包含同名不同大小/内容的图片，并把模式保留到待删除结果");
        var withHidden = await IdenticalImages.ScanAsync(root, [sample], true, null, default);
        check(withHidden.Matches.Count == 3 && withHidden.Matches.All(match => !match.Path.Contains("node_modules")), "隐藏开关与开发目录排除必须生效");
        var multiple = await IdenticalImages.ScanAsync(root, [sample, renamed, different], false, null, default);
        check(multiple.Matches.Count == 4, "多个同名不同内容的样本必须各自匹配，不能覆盖或重复返回同一文件");
        var capped = await IdenticalImages.ScanAsync(root, [sample], false, null, default, matchLimit: 1);
        check(capped.Limited && capped.Matches.Count == 1, "匹配上限必须明确报告不完整");
        var match = found.Matches.Single(match => match.Path == twin);
        check(await IdenticalImages.IsUnchangedAsync(root, match, default), "清理前完整内容和版本应通过校验");
        var timestamp = File.GetLastWriteTimeUtc(twin);
        await File.WriteAllTextAsync(twin, "new! bytes");
        File.SetLastWriteTimeUtc(twin, timestamp);
        check(!await IdenticalImages.IsUnchangedAsync(root, match, default), "即使长度和修改时间相同，变化的内容仍必须跳过");
        File.Delete(twin);
        check(!await IdenticalImages.IsUnchangedAsync(root, match, default), "扫描后消失的文件必须跳过");
        var nameMatch = nameOnly.Matches.Single(match => match.Path == different);
        check(await IdenticalImages.IsUnchangedAsync(root, nameMatch, default), "仅名称匹配结果应允许与样本内容不同的原文件通过删除前检查");
        timestamp = File.GetLastWriteTimeUtc(different);
        await File.WriteAllTextAsync(different, "new! bytes");
        File.SetLastWriteTimeUtc(different, timestamp);
        check(!await IdenticalImages.IsUnchangedAsync(root, nameMatch, default), "仅名称匹配仍须跳过扫描后变更的文件，即使大小和时间未变");
        var cappedNames = await IdenticalImages.ScanAsync(root, [sample], false, null, default, matchLimit: 1, mode: IdenticalImageMatchMode.NameOnly);
        check(cappedNames.Limited && cappedNames.Matches.Count == 1, "仅名称匹配也必须遵守结果上限");
        bool invalidMode = false;
        try { await IdenticalImages.ScanAsync(root, [sample], false, null, default, mode: (IdenticalImageMatchMode)99); }
        catch (ArgumentOutOfRangeException) { invalidMode = true; }
        check(invalidMode, "未知匹配模式不能静默退化为仅名称匹配");
        string outside = Path.Combine(fixture, "outside.png");
        await File.WriteAllTextAsync(outside, "same bytes");
        bool rejected = false;
        try { await IdenticalImages.ScanAsync(root, [outside], false, null, default); }
        catch (IOException) { rejected = true; }
        check(rejected, "样本不能逃出扫描根目录");
        var outsideMatch = match with { Path = outside };
        check(!await IdenticalImages.IsUnchangedAsync(root, outsideMatch, default), "清理范围校验应拒绝根目录之外的路径");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await IdenticalImages.ScanAsync(root, [sample], false, null, canceled.Token); check(false, "扫描应可取消"); }
        catch (OperationCanceledException) { check(true, "扫描取消"); }
        File.SetAttributes(hidden, FileAttributes.Directory);
    }
}
