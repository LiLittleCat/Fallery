using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Fallery;

public sealed partial class MainWindow
{
    private TreeView? _keyboardReturnTree;
    private string? _keyboardReturnFolder;
    private int _treeKeyboardRequest;

    private TreeView? FindInputTree(DependencyObject? source)
    {
        for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element == FolderTree || element == FavoritesTree) return (TreeView)element;
        return null;
    }

    private static TreeViewNode? FocusedTreeNode(TreeView tree, DependencyObject? source)
    {
        for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is TreeViewItem container && tree.NodeFromContainer(container) is { } node) return node;
        return tree.SelectedNode;
    }

    private static void FocusTreeNode(TreeView tree, TreeViewNode node, FocusState focusState = FocusState.Keyboard)
    {
        if (tree.SelectedNode != node) tree.SelectedNode = node;
        FindTreeList(tree)?.ScrollIntoView(node);
        tree.UpdateLayout();
        if (tree.ContainerFromNode(node) is Control container) container.Focus(focusState);
        else tree.Focus(focusState);
    }

    private void MoveTreeSelection(TreeView tree, TreeViewNode? current, VirtualKey key)
    {
        _treeKeyboardRequest++;
        if (current is not null && key == VirtualKey.Left)
        {
            if (current.IsExpanded) current.IsExpanded = false;
            else if (current.Parent is { } parent && !tree.RootNodes.Contains(current)) current = parent;
            FocusTreeNode(tree, current);
            return;
        }
        var visible = new List<TreeViewNode>();
        var pending = new Stack<TreeViewNode>(tree.RootNodes.Reverse());
        while (pending.TryPop(out var node))
        {
            visible.Add(node);
            if (node.IsExpanded) foreach (var child in node.Children.Reverse()) pending.Push(child);
        }
        if (visible.Count == 0) return;
        int index = current is null ? -1 : visible.IndexOf(current);
        int next = index < 0 ? 0 : Math.Clamp(index + (key == VirtualKey.Up ? -1 : 1), 0, visible.Count - 1);
        FocusTreeNode(tree, visible[next]);
    }

    private async Task EnterGalleryFromTreeAsync(TreeView tree, TreeViewNode? node)
    {
        int request = ++_treeKeyboardRequest;
        if (node?.Content is not FolderEntry entry) return;
        if (string.IsNullOrEmpty(entry.Path))
        {
            node.IsExpanded = true;
            if (node.Children.FirstOrDefault() is { } child) FocusTreeNode(tree, child);
            return;
        }
        _keyboardReturnTree = tree;
        _keyboardReturnFolder = entry.Path;
        int version = _navigationVersion + 1;
        await NavigateAsync(entry.Path);
        if (_closed || _closing || GalleryState.IsScanning || version != _navigationVersion || request != _treeKeyboardRequest
            || !string.Equals(GalleryState.CurrentFolder, NormalizeFolderPath(entry.Path), StringComparison.OrdinalIgnoreCase)) return;
        CancelBrowsePositionRestore();
        if (_selectedItem is null || !GalleryState.Items.Contains(_selectedItem)) SelectItem(GalleryState.Items.FirstOrDefault());
        if (_selectedItem is { } item) FocusGalleryItem(item);
        else GalleryScroller.Focus(FocusState.Keyboard);
    }

    private void FocusGalleryItem(MediaItem item)
    {
        int index = GalleryState.Items.IndexOf(item);
        if (index < 0) return;
        _selectionCursor = item.Path;
        if (IsFilmstrip)
        {
            double left = index * 128;
            if (left < GalleryScroller.HorizontalOffset || left + FilmstripTileWidth > GalleryScroller.HorizontalOffset + GalleryScroller.ViewportWidth)
                GalleryScroller.ChangeView(Math.Max(0, left - (GalleryScroller.ViewportWidth - FilmstripTileWidth) / 2), null, null, true);
        }
        else BringItemIntoView(item);
        GalleryScroller.UpdateLayout();
        if (Gallery.TryGetElement(index) is Control tile) tile.Focus(FocusState.Keyboard);
        else GalleryScroller.Focus(FocusState.Keyboard);
    }

    private async Task ReturnToFolderTreeAsync()
    {
        if (_closed || _closing) return;
        int request = ++_treeKeyboardRequest;
        int navigation = _navigationVersion;
        if (!_sidebarVisible) SidebarToggle_Click(this, new RoutedEventArgs());
        if (_keyboardReturnTree == FavoritesTree
            && string.Equals(_keyboardReturnFolder, GalleryState.CurrentFolder, StringComparison.OrdinalIgnoreCase)
            && _favoritesRoot.Children.FirstOrDefault(node => node.Content is FolderEntry entry
                && string.Equals(entry.Path, _keyboardReturnFolder, StringComparison.OrdinalIgnoreCase)) is { } favorite)
        { FocusTreeNode(FavoritesTree, favorite, FocusState.Pointer); return; }
        // 打开/关闭媒体并未切换目录，不重复展开、选择和滚动整条目录路径。
        if (GalleryState.CurrentFolder is { } folder && !IsCurrentTreeNode(_currentFolderNode, folder))
            await SynchronizeTreeAsync(folder, _lifetimeCts.Token);
        if (_closed || _closing || request != _treeKeyboardRequest || navigation != _navigationVersion) return;
        var target = _currentFolderNode ?? FolderTree.SelectedNode ?? FolderTree.RootNodes.FirstOrDefault();
        // 自动返回只保留节点选中底色；用户随后按方向键时再显示键盘焦点框。
        if (target is not null) FocusTreeNode(FolderTree, target, FocusState.Pointer);
        else FolderTree.Focus(FocusState.Pointer);
    }

    private bool IsCurrentTreeNode(TreeViewNode? node, string folder)
    {
        if (node?.Content is not FolderEntry entry
            || !string.Equals(NormalizeFolderPath(entry.Path), NormalizeFolderPath(folder), StringComparison.OrdinalIgnoreCase)) return false;
        while (node.Parent is { } parent && !FolderTree.RootNodes.Contains(node)) node = parent;
        return FolderTree.RootNodes.Contains(node);
    }

    private async Task CloseViewerAndReturnToTreeAsync()
    {
        CloseViewerCore(restoreFocus: false);
        await ReturnToFolderTreeAsync();
    }

    private async Task OpenKeyboardSelectionAsync()
    {
        var item = GalleryState.Items.FirstOrDefault(item => string.Equals(item.Path, _selectionCursor, StringComparison.OrdinalIgnoreCase)) ?? _selectedItem;
        if (item?.IsFolder == true)
        {
            int version = _navigationVersion + 1;
            await NavigateAsync(item.Path);
            if (_closed || _closing || version != _navigationVersion
                || !string.Equals(GalleryState.CurrentFolder, item.Path, StringComparison.OrdinalIgnoreCase)) return;
            CancelBrowsePositionRestore();
            if (_selectedItem is null) SelectItem(GalleryState.Items.FirstOrDefault());
            if (_selectedItem is { } selected) FocusGalleryItem(selected);
            else GalleryScroller.Focus(FocusState.Keyboard);
        }
        else if (item is not null) await ShowInViewerAsync(GalleryState.MediaItems.IndexOf(item));
    }
}
