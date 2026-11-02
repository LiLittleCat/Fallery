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
        if (AppIdentity.IsDevelopment) return new(null, AppText.Get("DevelopmentBuildsDoNotReceiveInstallerUpdatesDownloadThe"));
        if (string.IsNullOrWhiteSpace(AppIdentity.UpdateSource)) return new(null, AppText.Get("ThisBuildHasNoUpdateSourceDownloadANewer"));
        try
        {
            var manager = new UpdateManager(VelopackUpdateBackend.CreateSource(AppIdentity.UpdateSource, AppIdentity.Channel == "preview"),
                new UpdateOptions { ExplicitChannel = AppIdentity.UpdateChannel, AllowVersionDowngrade = false });
            if (!manager.IsInstalled || manager.IsPortable) return new(null, AppText.Get("UseTheInstalledAppForInAppUpdatesFor"));
            if (manager.AppId != AppIdentity.PackageId || Velopack.Locators.VelopackLocator.Current.Channel != AppIdentity.UpdateChannel)
                return new(null, AppText.Get("InstallationInformationDoesNotMatchThisBuildDownloadAnd"));
            return new(new VelopackUpdateBackend(manager, AppIdentity.PackageId));
        }
        catch (Exception ex) { return new(null, AppText.Format("CouldNotInitializeUpdates", ex.Message)); }
    }

}
