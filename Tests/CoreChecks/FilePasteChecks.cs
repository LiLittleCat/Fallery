using Fallery.Services;

internal static class FilePasteChecks
{
    internal static async Task RunAsync(string fixture, Action<bool, string> check)
    {
        string source = Directory.CreateDirectory(Path.Combine(fixture, "source")).FullName;
        string destination = Directory.CreateDirectory(Path.Combine(fixture, "destination")).FullName;
        string photo = Path.Combine(source, "照片.jpg");
        await File.WriteAllTextAsync(photo, "original bytes");
        var copied = await FilePaste.CopyAsync([photo], destination, default);
        check(copied.Errors.Count == 0 && copied.Copied.Count == 1
            && await File.ReadAllTextAsync(copied.Copied[0]) == "original bytes", "粘贴文件必须保留内容");
        var duplicate = await FilePaste.CopyAsync([photo], destination, default);
        check(duplicate.Errors.Count == 0 && duplicate.Copied[0] != copied.Copied[0]
            && File.Exists(copied.Copied[0]), "同名粘贴必须自动改名且不覆盖");
        var sameFolder = await FilePaste.CopyAsync([photo], source, default);
        check(sameFolder.Errors.Count == 0 && sameFolder.Copied[0] != photo && File.Exists(photo), "同目录粘贴应生成副本");
        string album = Directory.CreateDirectory(Path.Combine(source, "album")).FullName;
        Directory.CreateDirectory(Path.Combine(album, "nested", "empty"));
        await File.WriteAllTextAsync(Path.Combine(album, "nested", "video.mp4"), "video bytes");
        var folder = await FilePaste.CopyAsync([album], destination, default);
        check(folder.Errors.Count == 0 && Directory.Exists(Path.Combine(folder.Copied[0], "nested", "empty"))
            && await File.ReadAllTextAsync(Path.Combine(folder.Copied[0], "nested", "video.mp4")) == "video bytes",
            "粘贴文件夹必须保留后代文件及空目录");
        var folderAgain = await FilePaste.CopyAsync([album], destination, default);
        check(folderAgain.Errors.Count == 0 && folderAgain.Copied[0] != folder.Copied[0], "同名文件夹不能静默合并");
        foreach (string invalid in new[] { album, Path.Combine(album, "nested") })
        {
            var prevented = await FilePaste.CopyAsync([album], invalid, default);
            check(prevented.Copied.Count == 0 && prevented.Errors.Count == 1, "不能将文件夹递归复制到自身或后代");
        }
        var mixed = await FilePaste.CopyAsync([Path.Combine(source, "missing.jpg"), photo, photo], destination, default);
        check(mixed.Copied.Count == 1 && mixed.Errors.Count == 1, "失效剪贴板项目应报错但不阻止其他文件，重复路径只复制一次");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        int before = Directory.GetFiles(destination).Length;
        try { await FilePaste.CopyAsync([photo], destination, canceled.Token); check(false, "取消粘贴必须中止"); }
        catch (OperationCanceledException) { check(Directory.GetFiles(destination).Length == before, "取消不能生成新文件"); }
        await FileOperationChecks.RunAsync(Path.Combine(fixture, "operations"), check);
    }
}
