using Fallery.Models;
using Fallery.Services;

namespace Fallery;

public sealed partial class MainWindow
{
    private async Task<string> SynchronizeDeletedDataAsync(IReadOnlyCollection<string> paths, IReadOnlyCollection<FileVersion> versions)
    {
        _deleteSelectionReady = true;
        if (paths.Count == 0) return "";
        try
        {
            GalleryState.ClearCachedDirectories();
            await MediaCache.RemoveDeletedAsync(paths, versions);
            // 不启动全盘扫描；先停止旧构建，再裁剪已有索引，避免旧扫描结果把已删路径写回。
            _searchBuildCts?.Cancel();
            if (_searchIndexTask is not null) await _searchIndexTask;
            bool load = _searchIndex is null || _searchIndex.Status.Count == 0;
            EnsureSearchIndex();
            if (load) await _searchIndex!.EnsureLoadedAsync(CancellationToken.None);
            await _searchIndex!.RemovePathsAsync(paths, CancellationToken.None);
            return _searchIndex.Status.Message.Contains("未能保存", StringComparison.Ordinal) ? "；索引未能保存，请稍后刷新索引" : "";
        }
        catch (Exception) { return "；缓存或索引同步未完成，请稍后清理缓存并刷新索引"; }
    }
}
