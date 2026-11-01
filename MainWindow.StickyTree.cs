using Fallery.Controls;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Fallery;

public sealed partial class MainWindow
{
    private sealed record StickyTreeRow(TreeViewNode Node, FolderEntry Entry, string Glyph)
    {
        public string CollapseLabel => $"收起 {Entry.Name}";
    }

    private ListView? _stickyTreeList;
    private ScrollViewer? _stickyTreeScroller;
    private StickyTreeRow[] _stickyTreeRows = [];

    private void InitializeStickyTree()
    {
        FolderTree.Loaded += FolderTreeSticky_Loaded;
        FolderTree.Unloaded += FolderTreeSticky_Unloaded;
        FolderTree.LayoutUpdated += FolderTreeSticky_LayoutUpdated;
    }

    private void FolderTreeSticky_Loaded(object sender, RoutedEventArgs e) => UpdateStickyTree();
    private void FolderTreeSticky_Unloaded(object sender, RoutedEventArgs e) => DetachStickyTree();
    private void FolderTreeSticky_LayoutUpdated(object? sender, object e) => UpdateStickyTree();
    private void StickyTree_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateStickyTree();

    private void DetachStickyTree()
    {
        if (_stickyTreeScroller is not null) _stickyTreeScroller.ViewChanged -= StickyTree_ViewChanged;
        _stickyTreeScroller = null;
        _stickyTreeList = null;
        SetStickyTreeRows([]);
    }

    private static T? FindStickyTreeVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindStickyTreeVisual<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private void UpdateStickyTree()
    {
        if (_closed || _closing || !FolderTree.IsLoaded) return;
        _stickyTreeList ??= FindTreeList(FolderTree);
        if (_stickyTreeList is null) return;
        if (_stickyTreeScroller is null)
        {
            _stickyTreeScroller = FindStickyTreeVisual<ScrollViewer>(_stickyTreeList);
            if (_stickyTreeScroller is null) return;
            _stickyTreeScroller.ViewChanged += StickyTree_ViewChanged;
        }
        if (_stickyTreeScroller.VerticalOffset < 0.5 || FolderTree.ActualHeight < 64
            || _stickyTreeList.ItemsPanelRoot is not ItemsStackPanel panel || panel.FirstVisibleIndex < 0)
        { SetStickyTreeRows([]); return; }

        // 只查询当前已实现的第一行及其祖先，不枚举整树、不创建视口外容器。
        if (_stickyTreeList.ContainerFromIndex(panel.FirstVisibleIndex) is not TreeViewItem first
            || FolderTree.NodeFromContainer(first) is not { } anchor) return;
        var ancestors = new List<TreeViewNode>();
        for (TreeViewNode? node = anchor; node is not null && ancestors.Count < 128; node = node.Parent)
            ancestors.Add(node);
        ancestors.Reverse();
        var pinned = new List<StickyTreeRow>();
        foreach (var node in ancestors)
        {
            if (!node.IsExpanded || node.Content is not FolderEntry entry || entry.Path.Length == 0) continue;
            if (FolderTree.ContainerFromNode(node) is FrameworkElement container
                && container.TransformToVisual(FolderTree).TransformPoint(new Point()).Y >= -0.5) continue;
            string kind = FolderIconConverter.KindOf(entry.Path);
            pinned.Add(new(node, entry, FluentIcons.Glyph("Icon" + kind)));
        }
        // 深层目录保留最近三级；小窗口最多占树高度的三分之一。
        int limit = Math.Clamp((int)(FolderTree.ActualHeight / 3 / 32), 1, 3);
        SetStickyTreeRows(pinned.TakeLast(limit).ToArray());
    }

    private void SetStickyTreeRows(StickyTreeRow[] rows)
    {
        if (_stickyTreeRows.SequenceEqual(rows)) return;
        _stickyTreeRows = rows;
        FolderTreeStickyItems.ItemsSource = rows;
        FolderTreeStickyHeader.Visibility = rows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsAttachedStickyTreeNode(TreeViewNode node)
    {
        for (TreeViewNode? current = node; current is not null; current = current.Parent)
            if (FolderTree.RootNodes.Contains(current)) return true;
        return false;
    }

    private void CollapseStickyTreeNode(TreeViewNode node)
    {
        if (_closed || _closing || !node.IsExpanded || !IsAttachedStickyTreeNode(node)) return;
        node.IsExpanded = false;
        // 仅收起左侧分支，不导航、不打断右侧浏览/播放。把真实父行带回可见范围。
        FocusTreeNode(FolderTree, node, FocusState.Pointer);
        UpdateStickyTree();
    }

    private void StickyTreeCollapse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TreeViewNode node }) CollapseStickyTreeNode(node);
    }

    private async void StickyTreeNavigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TreeViewNode node } && IsAttachedStickyTreeNode(node)) await NavigateTreeNodeAsync(node);
    }

    private void FolderTreeStickyHeader_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_stickyTreeScroller is null) return;
        var point = e.GetCurrentPoint(FolderTreeStickyHeader);
        if (point.Properties.IsHorizontalMouseWheel) return;
        _stickyTreeScroller.ChangeView(null, Math.Clamp(_stickyTreeScroller.VerticalOffset - point.Properties.MouseWheelDelta / 120d * 96,
            0, _stickyTreeScroller.ScrollableHeight), null, true);
        e.Handled = true;
    }
}
