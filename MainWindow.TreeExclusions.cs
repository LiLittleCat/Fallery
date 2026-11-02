using Fallery.Controls;
using Fallery.Services;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private readonly List<string> _treeExcludedFolders = [];

    private bool IsTreeFolderExcluded(string path) =>
        _treeExcludedFolders.Any(folder => IsSameOrChildFolder(path, folder));

    private void ClearCurrentTreeSelection()
    {
        FolderTree.SelectedNode = null;
        _currentFolderNode = null;
    }

    private void ClearExcludedTreeSelection()
    {
        if (FolderTree.SelectedNode?.Content is FolderEntry selected && IsTreeFolderExcluded(selected.Path))
            FolderTree.SelectedNode = null;
        if (_currentFolderNode?.Content is FolderEntry current && IsTreeFolderExcluded(current.Path))
            _currentFolderNode = null;
    }

    private void ExcludeTreeFolder(string path)
    {
        if (_closed || _closing || IsTreeFolderExcluded(path)) return;
        _treeExcludedFolders.Add(NormalizeFolderPath(path));
        ClearExcludedTreeSelection();
        // 同一个目录可能同时出现在图片库、磁盘和收藏中，要移除所有入口。
        void Prune(IList<TreeViewNode> nodes)
        {
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (nodes[i].Content is FolderEntry entry && IsTreeFolderExcluded(entry.Path)) nodes.RemoveAt(i);
                else Prune(nodes[i].Children);
            }
        }
        Prune(FolderTree.RootNodes);
        RebuildFavorites();
        UpdateTreeToolbar();
        QueueSettingsSave();
        GalleryState.Status = AppText.Get("ExcludedFromTheFolderTreeRestoreItInFolder");
    }

    private async Task RestoreExcludedTreeFolderAsync(string path)
    {
        if (_closed || _closing || _treeExcludedFolders.RemoveAll(folder =>
            string.Equals(folder, path, StringComparison.OrdinalIgnoreCase)) == 0) return;
        QueueSettingsSave();
        UpdateTreeToolbar();
        RebuildFavorites();
        await (_rootsTask ??= LoadRootsAsync());
        await LoadRootsAsync();
        // 只刷新相关的已加载分支，未展开的节点仍保持懒加载。
        await RefreshWatchedTreeAsync(path, _navigationVersion);
        if (!_closed && !_closing && GalleryState.CurrentFolder is { } current)
            await SelectCurrentFolderAsync(current, _lifetimeCts.Token);
    }

    private async void TreeExclusions_Click(object sender, RoutedEventArgs e) => await ShowSettingsAsync(1);
}
