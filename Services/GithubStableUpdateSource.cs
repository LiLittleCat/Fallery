using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace Fallery.Services;

/// <summary>正式版读取公开 latest 清单，下载固定到已检查版本的发行标签。</summary>
public sealed class GithubStableUpdateSource(string repositoryUrl, IFileDownloader? downloader = null)
    : SimpleWebSource(repositoryUrl.TrimEnd('/') + "/releases/latest/download", downloader)
{
    private readonly string _repositoryUrl = repositoryUrl.TrimEnd('/');

    public override Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
        Action<int> progress, CancellationToken cancelToken)
    {
        // 发行脚本使用 v<版本> 标签；不能在下载时再次解析可变化的 latest。
        // 清单中的文件名只允许单个名称，防止绕过固定发行目录。
        if (releaseEntry.Version.IsPrerelease || string.IsNullOrWhiteSpace(releaseEntry.FileName)
            || releaseEntry.FileName.IndexOfAny(['/', '\\', ':', '?', '#', '%']) >= 0
            || releaseEntry.FileName is "." or "..")
            throw new InvalidDataException(AppText.Get("UpdatePackageProductOrVersionDoesNotMatch"));
        var pinned = new SimpleWebSource(_repositoryUrl + "/releases/download/v"
            + Uri.EscapeDataString(releaseEntry.Version.ToString()), Downloader, Timeout);
        return pinned.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
    }
}
