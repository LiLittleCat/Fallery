using Windows.Storage;

namespace Fallery.Services;

public static class FileRename
{
    public static async Task<string> RenameAsync(string path, bool folder, string name)
    {
        FolderCreation.ValidateName(name);
        path = Path.GetFullPath(path);
        if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) is null)
            throw new IOException("不能重命名磁盘根目录。");
        IStorageItem item = folder ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path);
        await item.RenameAsync(name, NameCollisionOption.FailIfExists);
        return item.Path;
    }
}
