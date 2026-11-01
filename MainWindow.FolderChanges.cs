using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private FolderChangeMonitor? _folderMonitor;
    private DispatcherQueueTimer? _folderChangeTimer;
    private string? _pendingFolderChange;
    private string? _liveRefreshFolder;
    private int _liveRefreshVersion;
    private double _liveRefreshOffset;
    private double _liveRefreshHorizontalOffset;
    private string? _liveRefreshSelection;
    private bool _refreshingFolderChanges;
    private bool IsLiveFolderRefresh => _liveRefreshFolder is not null && _liveRefreshVersion == _navigationVersion
        && string.Equals(_liveRefreshFolder, GalleryState.CurrentFolder, StringComparison.OrdinalIgnoreCase);

    private void InitializeFolderMonitoring()
    {
        if (_folderMonitor is not null) return;
        _folderMonitor = new FolderChangeMonitor();
        _folderChangeTimer = DispatcherQueue.CreateTimer();
        _folderChangeTimer.Interval = TimeSpan.FromMilliseconds(400);
        _folderChangeTimer.IsRepeating = false;
        _folderChangeTimer.Tick += FolderChanges_Tick;
        _folderMonitor.Changed += folder => DispatcherQueue.TryEnqueue(() => QueueFolderChange(folder));
        GalleryState.FolderCommitted += (folder, token) => _ = WatchFolderChangesAsync(folder);
    }

    private void QueueFolderChange(string folder)
    {
        if (_closed || _closing || !string.Equals(folder, GalleryState.CurrentFolder, StringComparison.OrdinalIgnoreCase)) return;
        _pendingFolderChange = folder;
        _folderChangeTimer?.Stop();
        _folderChangeTimer?.Start();
    }

    private async Task WatchFolderChangesAsync(string folder)
    {
        if (_folderMonitor is null || _closed) return;
        try { await _folderMonitor.WatchAsync(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!_closed && GalleryState.CurrentFolder == folder)
            {
                if (!await Task.Run(() => Directory.Exists(folder))) QueueFolderChange(folder);
                else GalleryState.Status += $" · 无法监听目录变化：{ex.Message}";
            }
        }
    }

    private async void FolderChanges_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_closed || _closing || _pendingFolderChange is not { } folder) return;
        if (!string.Equals(folder, GalleryState.CurrentFolder, StringComparison.OrdinalIgnoreCase))
        {
            _pendingFolderChange = null;
            return;
        }
        if (_refreshingFolderChanges || GalleryState.IsScanning || _deleting || _pasting)
        {
            sender.Start();
            return;
        }
        _pendingFolderChange = null;
        _refreshingFolderChanges = true;
        int version = _navigationVersion;
        try
        {
            string? existing = await Task.Run(() =>
            {
                string? candidate = folder;
                while (candidate is not null && !Directory.Exists(candidate)) candidate = System.IO.Path.GetDirectoryName(candidate);
                return candidate;
            });
            if (_closed || version != _navigationVersion) return;
            await RefreshWatchedTreeAsync(folder, version);
            if (_closed || version != _navigationVersion) return;
            if (existing != folder)
            {
                if (existing is not null) await NavigateAsync(existing);
                else { CloseViewer(); GalleryState.Status = "当前目录已不可用，请选择其他目录"; }
                return;
            }
            _liveRefreshFolder = folder;
            _liveRefreshVersion = version;
            _liveRefreshOffset = GalleryScroller.VerticalOffset;
            _liveRefreshHorizontalOffset = GalleryScroller.HorizontalOffset;
            _liveRefreshSelection = _selectedItem?.Path;
            // 直接刷新模型，不走会关闭查看器的导航入口；历史与仍存在的播放项保持不变。
            await GalleryState.NavigateAsync(folder, pushHistory: false, refresh: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_closed && version == _navigationVersion) GalleryState.Status = $"自动刷新失败：{ex.Message}";
        }
        finally { _liveRefreshFolder = null; _refreshingFolderChanges = false; }
    }

    private async Task RefreshWatchedTreeAsync(string folder, int version)
    {
        var selected = FolderTree.SelectedNode;
        var parent = System.IO.Path.GetDirectoryName(folder);
        List<TreeViewNode> targets = [];
        var pending = new Stack<TreeViewNode>(FolderTree.RootNodes);
        while (pending.TryPop(out var node))
        {
            foreach (var child in node.Children) pending.Push(child);
            if (node.Content is FolderEntry entry && (IsSameOrChildFolder(entry.Path, folder)
                || string.Equals(entry.Path, parent, StringComparison.OrdinalIgnoreCase)
                || IsSameOrChildFolder(folder, entry.Path))) targets.Add(node);
        }
        foreach (var node in targets)
        {
            if (_closed || version != _navigationVersion) return;
            if (_expandingNodes.TryGetValue(node, out var loading)) await loading;
            var entry = (FolderEntry)node.Content;
            bool loaded = !node.HasUnrealizedChildren || node.IsExpanded;
            bool showHidden = GalleryState.ShowHidden;
            var result = await Task.Run(() =>
            {
                bool exists = Directory.Exists(entry.Path);
                List<FolderChild>? children = null;
                try { if (exists && loaded) children = FolderScanner.GetChildFolders(entry.Path, _lifetimeCts.Token, showHidden); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 一个节点在枚举时消失或失去权限，不应阻止其他节点和主列表更新。
                    exists = Directory.Exists(entry.Path);
                }
                return (exists, children, modifiedTicks: exists ? FolderScanner.ReadFolderModifiedTicks(entry.Path) : null,
                    createdTicks: exists ? FolderScanner.ReadFolderCreatedTicks(entry.Path) : null);
            });
            if (_closed || version != _navigationVersion) return;
            if (!result.exists) { RemoveTreeNode(entry.Path); continue; }
            node.Content = entry with { ModifiedTicks = result.modifiedTicks, CreatedTicks = result.createdTicks,
                ShowModifiedTime = _treeShowModifiedTime, TimeField = _treeTimeField };
            if (result.children is null) continue;
            result.children.RemoveAll(child => IsTreeFolderExcluded(child.Path));
            var paths = result.children.Select(child => child.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (int i = node.Children.Count - 1; i >= 0; i--)
                if (node.Children[i].Content is FolderEntry child && !paths.Contains(child.Path)) node.Children.RemoveAt(i);
            for (int index = 0; index < result.children.Count; index++)
            {
                var (path, hasChildren) = result.children[index];
                var child = node.Children.FirstOrDefault(n => n.Content is FolderEntry info && string.Equals(info.Path, path, StringComparison.OrdinalIgnoreCase));
                if (child is null)
                {
                    child = CreateNode(path, System.IO.Path.GetFileName(path), result.children[index].ModifiedTicks, result.children[index].CreatedTicks);
                    child.HasUnrealizedChildren = hasChildren;
                    node.Children.Insert(index, child);
                }
                else if (node.Children.IndexOf(child) != index)
                {
                    node.Children.Remove(child);
                    node.Children.Insert(index, child);
                }
                if (child.Content is FolderEntry childEntry)
                    child.Content = childEntry with { ModifiedTicks = result.children[index].ModifiedTicks,
                        CreatedTicks = result.children[index].CreatedTicks, ShowModifiedTime = _treeShowModifiedTime, TimeField = _treeTimeField };
            }
            node.HasUnrealizedChildren = false;
            SortTreeChildren(node);
        }
        if (selected is not null && (selected.Parent is not null || FolderTree.RootNodes.Contains(selected)))
            FolderTree.SelectedNode = selected;
    }
}
