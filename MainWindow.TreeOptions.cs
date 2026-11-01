using System.Collections.Concurrent;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private MediaSortField _treeSortField;
    private bool _treeDescending;
    private bool _treeShowModifiedTime;
    private CancellationTokenSource? _treePresentationCts;

    private void UpdateTreeToolbar()
    {
        TreeSortNameItem.IsChecked = _treeSortField == MediaSortField.Name;
        TreeSortModifiedItem.IsChecked = _treeSortField == MediaSortField.Modified;
        TreeAscendingItem.IsChecked = !_treeDescending;
        TreeDescendingItem.IsChecked = _treeDescending;
        TreeModifiedToggle.IsChecked = _treeShowModifiedTime;
        NewFolderButton.IsEnabled = GalleryState.CurrentFolder is not null;
        TreeExclusionsItem.Text = $"管理排除的文件夹（{_treeExcludedFolders.Count}）";
    }

    private async void TreeSort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string tag }) return;
        if (Enum.TryParse<MediaSortField>(tag, out var field))
            await SetTreeOptionsAsync(field, _treeDescending, _treeShowModifiedTime);
        else await SetTreeOptionsAsync(_treeSortField, tag == "Descending", _treeShowModifiedTime);
    }

    private async void TreeModified_Click(object sender, RoutedEventArgs e) =>
        await SetTreeOptionsAsync(_treeSortField, _treeDescending, TreeModifiedToggle.IsChecked == true);

    private async Task SetTreeOptionsAsync(MediaSortField field, bool descending, bool showModifiedTime)
    {
        if (_closed || _closing || field is not (MediaSortField.Name or MediaSortField.Modified)) return;
        _treeSortField = field;
        _treeDescending = descending;
        _treeShowModifiedTime = showModifiedTime;
        UpdateTreeToolbar();
        QueueSettingsSave();
        await RefreshTreePresentationAsync();
    }

    private List<TreeViewNode> LoadedFolderNodes()
    {
        List<TreeViewNode> nodes = [];
        var pending = new Stack<TreeViewNode>(FolderTree.RootNodes.Concat(_favoritesRoot.Children));
        while (pending.TryPop(out var node))
        {
            nodes.Add(node);
            foreach (var child in node.Children) pending.Push(child);
        }
        return nodes;
    }

    private async Task RefreshTreePresentationAsync()
    {
        if (_closed || _closing) return;
        _treePresentationCts?.Cancel();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _treePresentationCts = cts;
        try
        {
            var nodes = LoadedFolderNodes();
            var dates = new ConcurrentDictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
            if (_treeShowModifiedTime || _treeSortField == MediaSortField.Modified)
            {
                var paths = nodes.Select(node => ((FolderEntry)node.Content).Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                // 只读取已加载节点，不展开子树；网络目录的元数据也不能阻塞 UI。
                await Task.Run(() => Parallel.ForEach(paths,
                    new ParallelOptions { CancellationToken = cts.Token, MaxDegreeOfParallelism = 4 },
                    path => dates[path] = FolderScanner.ReadFolderModifiedTicks(path)), cts.Token);
            }
            cts.Token.ThrowIfCancellationRequested();
            if (_closed || _closing) return;
            var selected = FolderTree.SelectedNode;
            var favorite = FavoritesTree.SelectedNode;
            foreach (var node in nodes)
            {
                if (node.Content is not FolderEntry entry) continue;
                node.Content = entry with { ShowModifiedTime = _treeShowModifiedTime,
                    ModifiedTicks = dates.TryGetValue(entry.Path, out var ticks) ? ticks : entry.ModifiedTicks };
                SortTreeChildren(node);
            }
            SortTreeChildren(_favoritesRoot);
            FolderTree.SelectedNode = selected;
            FavoritesTree.SelectedNode = favorite;
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_treePresentationCts, cts)) _treePresentationCts = null; }
    }

    private void SortTreeChildren(TreeViewNode parent)
    {
        if (parent.Children.Count < 2) return;
        var selected = FolderTree.SelectedNode;
        var favorite = FavoritesTree.SelectedNode;
        var sorted = parent.Children.OrderBy(node => (FolderEntry)node.Content,
            Comparer<FolderEntry>.Create(CompareTreeEntries)).ToArray();
        for (int index = 0; index < sorted.Length; index++)
        {
            if (ReferenceEquals(parent.Children[index], sorted[index])) continue;
            bool expanded = sorted[index].IsExpanded;
            parent.Children.Remove(sorted[index]);
            parent.Children.Insert(index, sorted[index]);
            sorted[index].IsExpanded = expanded;
        }
        FolderTree.SelectedNode = selected;
        FavoritesTree.SelectedNode = favorite;
    }

    private int CompareTreeEntries(FolderEntry left, FolderEntry right)
    {
        int result = 0;
        if (_treeSortField == MediaSortField.Modified)
        {
            if (left.ModifiedTicks.HasValue != right.ModifiedTicks.HasValue) return left.ModifiedTicks.HasValue ? -1 : 1;
            result = Nullable.Compare(left.ModifiedTicks, right.ModifiedTicks);
        }
        if (result == 0) result = FolderScanner.CompareNames(left.Name, right.Name);
        return _treeDescending ? -Math.Sign(result) : result;
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _closing || _settingsDialog is not null || _deleting || _pasting || GalleryState.CurrentFolder is not { } parent) return;
        StopHoverPreview();
        var input = new TextBox { Text = "新建文件夹", Header = "文件夹名称", MinWidth = 300, MaxLength = 255 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = $"创建位置：{parent}", TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        content.Children.Add(input);
        content.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme,
            Title = "新建文件夹", Content = content, PrimaryButtonText = "创建", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        string? created = null;
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                created = await FolderCreation.CreateAsync(parent, input.Text);
                args.Cancel = false;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                error.Text = ex.Message;
            }
            finally { dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        _settingsDialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_settingsDialog, dialog)) _settingsDialog = null; }
        if (created is not null && !_closed && !_closing)
        {
            await NavigateAsync(created);
            if (GalleryState.CurrentFolder == created) GalleryState.Status = "已创建文件夹";
        }
    }
}
