using Fallery.Services;

internal static class FileOperationChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        string target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
        string file = Path.Combine(source, "a.jpg"), other = Path.Combine(target, "a.jpg");
        await File.WriteAllTextAsync(file, "new");
        await File.WriteAllTextAsync(other, "old");
        Task<FilePasteResult> Transfer(string path, bool move, FileConflictChoice choice) =>
            FilePaste.TransferAsync([path], target, move, _ => Task.FromResult(choice), default);
        var skipped = await Transfer(file, true, FileConflictChoice.Skip);
        check(skipped.Skipped == 1 && File.Exists(file) && await File.ReadAllTextAsync(other) == "old", "跳过移动冲突必须保留来源和目标");
        var canceled = await Transfer(file, true, FileConflictChoice.Cancel);
        check(canceled.Canceled && File.Exists(file) && await File.ReadAllTextAsync(other) == "old", "取消移动冲突不能修改文件");
        var replaced = await Transfer(file, false, FileConflictChoice.Replace);
        check(replaced.Errors.Count == 0 && File.Exists(file) && await File.ReadAllTextAsync(other) == "new", "替换复制仅覆盖目标，保留来源");
        var moved = await Transfer(file, true, FileConflictChoice.KeepBoth);
        check(moved.Errors.Count == 0 && moved.Copied.Count == 1 && !File.Exists(file)
            && moved.Copied[0] != other && await File.ReadAllTextAsync(moved.Copied[0]) == "new", "移动保留两者应返回新名称且移除来源");
        var same = await FilePaste.TransferAsync([other], target, true, _ => throw new Exception("同目录移动不应询问替换"), default);
        check(same.Skipped == 1 && File.Exists(other), "同目录剪切粘贴不能覆盖或删除自身");
        await File.WriteAllTextAsync(file, "replacement");
        var moveReplace = await Transfer(file, true, FileConflictChoice.Replace);
        check(moveReplace.Errors.Count == 0 && !File.Exists(file) && await File.ReadAllTextAsync(other) == "replacement", "确认替换的移动应保留新内容");

        string album = Directory.CreateDirectory(Path.Combine(source, "album")).FullName;
        string targetAlbum = Directory.CreateDirectory(Path.Combine(target, "album")).FullName;
        Directory.CreateDirectory(Path.Combine(album, "empty"));
        await File.WriteAllTextAsync(Path.Combine(album, "keep.jpg"), "source keep");
        await File.WriteAllTextAsync(Path.Combine(targetAlbum, "keep.jpg"), "target keep");
        await File.WriteAllTextAsync(Path.Combine(album, "move.jpg"), "move");
        var partial = await FilePaste.TransferAsync([album], target, true,
            conflict => Task.FromResult(conflict.SourceFolder ? FileConflictChoice.Replace : FileConflictChoice.Skip), default);
        check(partial.Errors.Count == 0 && partial.Skipped == 1 && File.Exists(Path.Combine(album, "keep.jpg"))
            && !File.Exists(Path.Combine(album, "move.jpg")) && File.Exists(Path.Combine(targetAlbum, "move.jpg")),
            "文件夹合并移动应保留跳过的源文件，仅移除成功移动的文件");
        check(Directory.Exists(Path.Combine(targetAlbum, "empty")) && !Directory.Exists(Path.Combine(album, "empty")), "移动保留空目录结构");
        check(await File.ReadAllTextAsync(Path.Combine(targetAlbum, "keep.jpg")) == "target keep", "合并不能覆盖未确认的同名文件");
        var merged = await Transfer(album, true, FileConflictChoice.Replace);
        check(merged.Errors.Count == 0 && !Directory.Exists(album)
            && await File.ReadAllTextAsync(Path.Combine(targetAlbum, "keep.jpg")) == "source keep", "完成合并移动后清理空源目录");

        string conflictFolder = Directory.CreateDirectory(Path.Combine(source, "a.jpg")).FullName;
        var wrongType = await Transfer(conflictFolder, true, FileConflictChoice.Replace);
        check(wrongType.Errors.Count == 1 && Directory.Exists(conflictFolder) && File.Exists(other), "禁止以文件夹替换文件");
        var recursive = await FilePaste.TransferAsync([source], conflictFolder, true, _ => Task.FromResult(FileConflictChoice.Replace), default);
        check(recursive.Errors.Count == 1 && Directory.Exists(source), "禁止移动到自身后代");

        string rename = Path.Combine(source, "rename.jpg");
        await File.WriteAllTextAsync(rename, "rename bytes");
        string renamed = await FileRename.RenameAsync(rename, false, "renamed.jpg");
        check(!File.Exists(rename) && await File.ReadAllTextAsync(renamed) == "rename bytes", "重命名保留内容并返回新路径");
        string occupied = Path.Combine(source, "occupied.jpg");
        await File.WriteAllTextAsync(occupied, "do not replace");
        bool rejected = false;
        try { await FileRename.RenameAsync(renamed, false, "occupied.jpg"); }
        catch { rejected = true; }
        check(rejected && File.Exists(renamed) && await File.ReadAllTextAsync(occupied) == "do not replace", "重命名不能覆盖现有文件");
        foreach (string invalid in new[] { "CON.jpg", "../escape.jpg", "trailing.", " " })
        {
            rejected = false;
            try { await FileRename.RenameAsync(renamed, false, invalid); }
            catch (ArgumentException) { rejected = true; }
            check(rejected && File.Exists(renamed), "无效名称不能修改文件：" + invalid);
        }
        string folderRenamed = await FileRename.RenameAsync(conflictFolder, true, "renamed-folder");
        check(Directory.Exists(folderRenamed) && !Directory.Exists(conflictFolder), "文件夹重命名应更新路径");
    }
}
