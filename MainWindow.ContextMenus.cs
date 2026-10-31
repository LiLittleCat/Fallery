using Fallery.Controls;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;

namespace Fallery;

public sealed partial class MainWindow
{
    private MediaItem? _gestureItem;
    private long _lastRightRelease;
    private MenuFlyout? _itemMenu;

    private void CloseItemMenu()
    {
        _itemMenu?.Hide();
        _itemMenu = null;
    }

    private static MediaItem? FindMediaItem(DependencyObject? source)
    {
        for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is MediaTile tile) return tile.Item;
        return null;
    }

    private void Tile_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (sender is not MediaTile { Item: { } item } tile || _gesturePointer is not null) return;
        bool positioned = e.TryGetPosition(tile, out var point);
        if (positioned && Environment.TickCount64 - _lastRightRelease < 500) return;
        ShowItemMenu(tile, positioned ? point : null, item.Path, item.IsFolder);
    }

    private void FolderTree_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is not TreeViewItem container || FolderTree.NodeFromContainer(container) is not { Content: FolderEntry entry } node) continue;
            e.Handled = true;
            ShowItemMenu(container, e.TryGetPosition(container, out var point) ? point : null, entry.Path, true, node, !FolderTree.RootNodes.Contains(node));
            return;
        }
        if (!e.TryGetPosition(FolderTree, out _) && FolderTree.SelectedNode is { Content: FolderEntry selected } selectedNode)
        {
            e.Handled = true;
            ShowItemMenu(FolderTree, null, selected.Path, true, selectedNode, !FolderTree.RootNodes.Contains(selectedNode));
        }
    }

    private void Viewer_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (ViewerState.CurrentItem is not { } item) return;
        e.Handled = true;
        ShowItemMenu(Viewer, e.TryGetPosition(Viewer, out var point) ? point : null, item.Path, false);
    }

    private void ShowItemMenu(FrameworkElement target, Point? point, string path, bool folder, TreeViewNode? node = null, bool allowDelete = true)
    {
        StopHoverPreview();
        CloseItemMenu();
        var menu = BuildItemMenu(path, folder, node, allowDelete);
        _itemMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_itemMenu, menu)) _itemMenu = null; };
        menu.ShowAt(target, new FlyoutShowOptions { Position = point });
    }

    private MenuFlyout BuildItemMenu(string path, bool folder, TreeViewNode? node = null, bool allowDelete = true)
    {
        var menu = new MenuFlyout();
        void Add(string text, string key, Func<Task> action)
        {
            string icon = key switch
            {
                "open" => folder ? "IconFolder" : "IconOpen",
                "expand" => node?.IsExpanded == true ? "IconCollapse" : "IconExpand",
                "reveal" => "IconOpenFolder",
                "copy-path" => "IconCopy",
                "copy-file" => "IconCopyFile",
                "delete" => "IconDelete",
                _ => "IconRefresh",
            };
            var entry = new MenuFlyoutItem { Text = text, Tag = key, Icon = FluentIcons.Create(icon) };
            entry.Click += async (_, _) =>
            {
                try { await action(); }
                catch (Exception ex) { if (!_closed) GalleryState.Status = $"操作失败：{ex.Message}"; }
            };
            menu.Items.Add(entry);
        }
        Add(folder ? "打开文件夹" : "打开", "open", async () =>
        {
            if (folder) await NavigateAsync(path);
            else
            {
                int index = GalleryState.MediaItems.FindIndex(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) await ShowInViewerAsync(index);
            }
        });
        if (node is not null)
            Add(node.IsExpanded ? "折叠" : "展开", "expand", async () =>
            {
                if (node.IsExpanded) node.IsExpanded = false;
                else { await EnsureChildrenLoadedAsync(node); node.IsExpanded = true; }
            });
        Add("在资源管理器中定位", "reveal", () => RevealPathAsync(path, folder));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("复制完整路径", "copy-path", () =>
        {
            var data = new DataPackage();
            data.SetText(path);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            GalleryState.Status = "已复制完整路径";
            return Task.CompletedTask;
        });
        Add(folder ? "复制文件夹" : "复制文件", "copy-file", async () =>
        {
            IStorageItem item = folder ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path);
            var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            data.SetStorageItems(new[] { item });
            Clipboard.SetContent(data);
            Clipboard.Flush();
            GalleryState.Status = "已复制，可在资源管理器中粘贴";
        });
        if (folder)
            Add("刷新此目录", "refresh", async () =>
            {
                if (node is not null)
                {
                    if (_expandingNodes.TryGetValue(node, out var pending)) await pending;
                    node.Children.Clear();
                    node.HasUnrealizedChildren = true;
                    await EnsureChildrenLoadedAsync(node);
                }
                await NavigateAsync(path, refresh: true);
            });
        // 与资源管理器一致：按住 Shift 点击为永久删除；磁盘根目录不提供删除。
        if (allowDelete && System.IO.Path.GetPathRoot(path) is { } root
            && !string.Equals(System.IO.Path.TrimEndingDirectorySeparator(path), System.IO.Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            Add("删除", "delete", () => DeleteWithConfirmationAsync(path, folder, IsShiftDown()));
            ((MenuFlyoutItem)menu.Items[^1]).KeyboardAcceleratorTextOverride = "Delete";
        }
        return menu;
    }

    private static async Task RevealPathAsync(string path, bool folder)
    {
        string? parent = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path));
        if (parent is null)
        {
            if (!await Launcher.LaunchFolderPathAsync(path)) throw new IOException("无法打开资源管理器。");
            return;
        }
        var directory = await StorageFolder.GetFolderFromPathAsync(parent);
        IStorageItem item = folder ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path);
        var options = new FolderLauncherOptions();
        options.ItemsToSelect.Add(item);
        if (!await Launcher.LaunchFolderAsync(directory, options)) throw new IOException("无法打开资源管理器。");
    }
}
