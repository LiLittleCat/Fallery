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
        GalleryState.Status = "已从文件树排除，可在文件树设置中恢复";
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

    private async void TreeExclusions_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _closing || _settingsDialog is not null) return;
        StopHoverPreview();
        var list = new ListView { MinWidth = 320, MaxWidth = 480, MaxHeight = 300, SelectionMode = ListViewSelectionMode.Single };
        var selectedPath = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
        var empty = new TextBlock { Text = "没有排除的文件夹" };
        var content = new StackPanel { Spacing = 12, MaxWidth = 480 };
        content.Children.Add(new TextBlock { Text = "仅隐藏左侧文件树及收藏中的目录，不删除文件。排除父目录时，其子目录也会隐藏。", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(empty);
        content.Children.Add(list);
        content.Children.Add(selectedPath);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme,
            Title = "排除的文件夹", Content = content, PrimaryButtonText = "取消排除", CloseButtonText = "完成", IsPrimaryButtonEnabled = false };
        void RefreshList()
        {
            list.ItemsSource = _treeExcludedFolders.ToArray();
            empty.Visibility = _treeExcludedFolders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_treeExcludedFolders.Count > 0) list.SelectedIndex = 0;
        }
        list.SelectionChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = list.SelectedItem is string;
            selectedPath.Text = list.SelectedItem as string ?? "";
        };
        RefreshList();
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (list.SelectedItem is not string path) return;
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            try { await RestoreExcludedTreeFolderAsync(path); }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { if (!_closed) GalleryState.Status = $"已取消排除，刷新目录失败：{ex.Message}"; }
            finally { RefreshList(); deferral.Complete(); }
        };
        _settingsDialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_settingsDialog, dialog)) _settingsDialog = null; }
    }
}
