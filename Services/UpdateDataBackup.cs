namespace Fallery.Services;

public static class UpdateDataBackup
{
    public static Task SaveAsync(string dataDirectory) => Task.Run(() =>
    {
        // 每次保留升级前的小型偏好备份，索引和图片缓存可以重建，无需复制。
        string directory = Path.Combine(dataDirectory, "Backups", $"before-update-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        foreach (string name in new[] { "settings.json", "resume.json" })
        {
            string source = Path.Combine(dataDirectory, name);
            if (File.Exists(source)) File.Copy(source, Path.Combine(directory, name));
        }
        File.WriteAllText(Path.Combine(directory, "version.txt"), AppIdentity.Version);
    });
}
