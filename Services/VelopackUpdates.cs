using Velopack;

namespace Fallery.Services;

public static class VelopackUpdates
{
    private static readonly Lazy<AppUpdateService> Service = new(CreateService);
    public static AppUpdateService Shared => Service.Value;

    private static string Executable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Fallery.exe");

    internal static void RegisterShell() => RunShellHook(() => FileAssociation.Register(Executable, FolderScanner.SupportedExtensions));
    internal static void UnregisterShell() => RunShellHook(() => FileAssociation.Unregister(FolderScanner.SupportedExtensions, expectedExePath: Executable));

    private static void RunShellHook(Action action)
    {
        if (AppIdentity.IsDevelopment) return;
        try { action(); }
        catch (Exception ex)
        {
            // 安装钩子不能显示窗口；保留明确诊断，启动后可在设置中重新注册。
            try
            {
                Directory.CreateDirectory(AppIdentity.DataDirectory);
                File.AppendAllText(Path.Combine(AppIdentity.DataDirectory, "update-hooks.log"), $"{DateTimeOffset.Now:O} {ex}\n");
            }
            catch { }
            Environment.ExitCode = 1;
        }
    }

    private static AppUpdateService CreateService()
    {
        if (AppIdentity.IsDevelopment) return new(null, "开发版不接收安装版更新，请从发行页面下载正式版。");
        if (string.IsNullOrWhiteSpace(AppIdentity.UpdateSource)) return new(null, "此版本没有配置更新地址，请从发行页面下载新版。");
        try
        {
            var manager = new UpdateManager(VelopackUpdateBackend.CreateSource(AppIdentity.UpdateSource, AppIdentity.Channel == "preview"),
                new UpdateOptions { ExplicitChannel = AppIdentity.UpdateChannel, AllowVersionDowngrade = false });
            if (!manager.IsInstalled || manager.IsPortable) return new(null, "请使用安装版进行应用内更新；解压运行版请从发行页面下载新版。");
            if (manager.AppId != AppIdentity.PackageId || Velopack.Locators.VelopackLocator.Current.Channel != AppIdentity.UpdateChannel)
                return new(null, "安装信息与当前版本不匹配，请重新下载安装包。");
            return new(new VelopackUpdateBackend(manager, AppIdentity.PackageId));
        }
        catch (Exception ex) { return new(null, $"无法初始化更新：{ex.Message}"); }
    }

}
