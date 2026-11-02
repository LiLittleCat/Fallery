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

    private async void TreeExclusions_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _closing || _settingsDialog is not null) return;
        StopHoverPreview();
        var list = new ListView { MinWidth = 320, MaxWidth = 480, MaxHeight = 300, SelectionMode = ListViewSelectionMode.Single };
        var selectedPath = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
        var empty = LiveText.Set(new TextBlock { Text = AppText.Get("NoExcludedFolders") }, "Text", localized => localized.Text = AppText.Get("NoExcludedFolders"));
        var content = new StackPanel { Spacing = 12, MaxWidth = 480 };
        content.Children.Add(LiveText.Set(new TextBlock { Text = AppText.Get("OnlyHidesFoldersFromTheFolderTreeAndFavorites"), TextWrapping = TextWrapping.Wrap }, "Text", localized => localized.Text = AppText.Get("OnlyHidesFoldersFromTheFolderTreeAndFavorites")));
        content.Children.Add(empty);
        content.Children.Add(list);
        content.Children.Add(selectedPath);
        var dialog = LiveText.Set(LiveText.Set(LiveText.Set(new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme,
            Title = AppText.Get("ExcludedFolders"), Content = content, PrimaryButtonText = AppText.Get("RestoreExcludedFolder"), CloseButtonText = AppText.Get("Done"), IsPrimaryButtonEnabled = false }, "Title", localized => localized.Title = AppText.Get("ExcludedFolders")), "PrimaryButtonText", localized => localized.PrimaryButtonText = AppText.Get("RestoreExcludedFolder")), "CloseButtonText", localized => localized.CloseButtonText = AppText.Get("Done"));
        void RefreshList()
        {
            LiveText.SetOnce(list, "ItemsSource", localized => localized.ItemsSource = _treeExcludedFolders.ToArray());
            empty.Visibility = _treeExcludedFolders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_treeExcludedFolders.Count > 0) list.SelectedIndex = 0;
        }
        list.SelectionChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = list.SelectedItem is string;
            LiveText.SetOnce(selectedPath, "Text", localized => localized.Text = list.SelectedItem as string ?? "");
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
            { if (!_closed) GalleryState.Status = AppText.Format("ExclusionRemovedButFolderRefreshFailed", ex.Message); }
            finally { RefreshList(); deferral.Complete(); }
        };
        _settingsDialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_settingsDialog, dialog)) _settingsDialog = null; }
    }
}
