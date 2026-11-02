using Velopack;
using Velopack.Sources;

namespace Fallery.Services;

public sealed class VelopackUpdateBackend(UpdateManager manager, string packageId) : IAppUpdateBackend
{
    public static IUpdateSource CreateSource(string source, bool preview)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("更新地址必须是无凭据的 HTTPS 地址。");
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            if (uri.AbsolutePath.Trim('/').Split('/').Length != 2) throw new ArgumentException("GitHub 更新地址应为仓库首页。");
            return new GithubSource(source.TrimEnd('/'), null, preview);
        }
        return new SimpleWebSource(source.TrimEnd('/'));
    }

    private AppUpdateRelease Wrap(VelopackAsset asset, object payload)
    {
        if (asset.PackageId != packageId || asset.Version <= manager.CurrentVersion)
            throw new InvalidDataException("更新包的产品或版本不匹配。");
        return new(asset.Version.ToString(), asset.NotesMarkdown ?? "", payload);
    }

    public AppUpdateRelease? Pending => manager.UpdatePendingRestart is { } asset ? Wrap(asset, asset) : null;
    public async Task<AppUpdateRelease?> CheckAsync(CancellationToken ct)
    {
        var update = await manager.CheckForUpdatesAsync().WaitAsync(ct);
        return update is null ? null : Wrap(update.TargetFullRelease, update);
    }

    public Task DownloadAsync(AppUpdateRelease release, Action<int> progress, CancellationToken ct)
        => manager.DownloadUpdatesAsync((UpdateInfo)release.Payload, progress, ct);

    public void ScheduleRestart(AppUpdateRelease release)
    {
        var asset = release.Payload is UpdateInfo info ? info.TargetFullRelease : (VelopackAsset)release.Payload;
        Wrap(asset, asset);
        manager.WaitExitThenApplyUpdates(asset, silent: false, restart: true);
    }
}
