using Velopack;
using Velopack.Sources;

namespace Fallery.Services;

public sealed class VelopackUpdateBackend(UpdateManager manager, string packageId) : IAppUpdateBackend
{
    public static IUpdateSource CreateSource(string source, bool preview, IFileDownloader? downloader = null)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException(AppText.Get("TheUpdateSourceMustBeAnHTTPSURLWithout"));
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            if (uri.AbsolutePath.Trim('/').Split('/').Length != 2) throw new ArgumentException(AppText.Get("TheGitHubUpdateSourceMustBeARepositoryURL"));
            // 正式版通过公开发行清单检查，不消耗按出口 IP 计算的匿名 API 配额。
            // latest 不包含预览版，预览通道仍使用 GitHub 的发行发现逻辑。
            return preview ? new GithubSource(source.TrimEnd('/'), null, true, downloader)
                : new GithubStableUpdateSource(source, downloader);
        }
        return new SimpleWebSource(source.TrimEnd('/'), downloader);
    }

    private AppUpdateRelease Wrap(VelopackAsset asset, object payload)
    {
        if (asset.PackageId != packageId || asset.Version <= manager.CurrentVersion)
            throw new InvalidDataException(AppText.Get("UpdatePackageProductOrVersionDoesNotMatch"));
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
