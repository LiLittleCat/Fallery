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
    private FolderTimeField _treeTimeField;
    private CancellationTokenSource? _treePresentationCts;

    private void UpdateTreeToolbar()
    {
        TreeSortNameItem.IsChecked = _treeSortField == MediaSortField.Name;
        TreeSortModifiedItem.IsChecked = _treeSortField == MediaSortField.Modified;
        TreeSortCreatedItem.IsChecked = _treeSortField == MediaSortField.Created;
        TreeAscendingItem.IsChecked = !_treeDescending;
        TreeDescendingItem.IsChecked = _treeDescending;
        TreeTimeHiddenItem.IsChecked = !_treeShowModifiedTime;
        TreeTimeModifiedItem.IsChecked = _treeShowModifiedTime && _treeTimeField == FolderTimeField.Modified;
        TreeTimeCreatedItem.IsChecked = _treeShowModifiedTime && _treeTimeField == FolderTimeField.Created;
        TreeTimeIcon.Glyph = Controls.FluentIcons.Glyph(_treeTimeField == FolderTimeField.Created ? "IconSortCreated" : "IconSortModified");
        string timeLabel = !_treeShowModifiedTime ? "不显示" : _treeTimeField == FolderTimeField.Created ? "创建时间" : "修改时间";
        ToolTipService.SetToolTip(TreeTimeButton, $"文件夹时间：{timeLabel}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TreeTimeButton, $"文件夹时间：{timeLabel}");
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

    private async void TreeTime_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string tag }) return;
        if (tag == "None") await SetTreeTimeAsync(_treeTimeField, false);
        else if (Enum.TryParse<FolderTimeField>(tag, out var field)) await SetTreeTimeAsync(field, true);
    }

    private async Task SetTreeTimeAsync(FolderTimeField field, bool visible)
    {
        if (_closed || _closing || !Enum.IsDefined(field)) return;
        _treeTimeField = field;
        await SetTreeOptionsAsync(_treeSortField, _treeDescending, visible);
    }

    private async Task SetTreeOptionsAsync(MediaSortField field, bool descending, bool showModifiedTime)
    {
        if (_closed || _closing || field is not (MediaSortField.Name or MediaSortField.Modified or MediaSortField.Created)) return;
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
            var dates = new ConcurrentDictionary<string, (long? Modified, long? Created)>(StringComparer.OrdinalIgnoreCase);
            if (_treeShowModifiedTime || _treeSortField is MediaSortField.Modified or MediaSortField.Created)
            {
                var paths = nodes.Select(node => ((FolderEntry)node.Content).Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                // 只读取已加载节点，不展开子树；网络目录的元数据也不能阻塞 UI。
                await Task.Run(() => Parallel.ForEach(paths,
                    new ParallelOptions { CancellationToken = cts.Token, MaxDegreeOfParallelism = 4 },
                    path => dates[path] = (FolderScanner.ReadFolderModifiedTicks(path), FolderScanner.ReadFolderCreatedTicks(path))), cts.Token);
            }
            cts.Token.ThrowIfCancellationRequested();
            if (_closed || _closing) return;
            var selected = FolderTree.SelectedNode;
            var favorite = FavoritesTree.SelectedNode;
            foreach (var node in nodes)
            {
                if (node.Content is not FolderEntry entry) continue;
                bool hasDates = dates.TryGetValue(entry.Path, out var ticks);
                node.Content = entry with { ShowModifiedTime = _treeShowModifiedTime, TimeField = _treeTimeField,
                    ModifiedTicks = hasDates ? ticks.Modified : entry.ModifiedTicks,
                    CreatedTicks = hasDates ? ticks.Created : entry.CreatedTicks };
            }
            // 先更新全部节点的日期，再排同级节点；父节点先遍历到时不能拿子节点的旧日期排序。
            foreach (var node in nodes) SortTreeChildren(node);
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
        if (_treeSortField is MediaSortField.Modified or MediaSortField.Created)
        {
            var leftTicks = _treeSortField == MediaSortField.Created ? left.CreatedTicks : left.ModifiedTicks;
            var rightTicks = _treeSortField == MediaSortField.Created ? right.CreatedTicks : right.ModifiedTicks;
            if (leftTicks.HasValue != rightTicks.HasValue) return leftTicks.HasValue ? -1 : 1;
            result = Nullable.Compare(leftTicks, rightTicks);
        }
        if (result == 0) result = FolderScanner.CompareNames(left.Name, right.Name);
        return _treeDescending ? -Math.Sign(result) : result;
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (GalleryState.CurrentFolder is { } parent) await ShowNewFolderDialogAsync(parent);
    }

    private async Task ShowNewFolderDialogAsync(string parent)
    {
        if (_closed || _closing || _settingsDialog is not null || _deleting || _pasting) return;
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
