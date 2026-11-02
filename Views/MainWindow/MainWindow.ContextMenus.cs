using Fallery.Services;
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
        if (!ViewerState.IsOpen || ViewerState.CurrentItem is null) return;
        e.Handled = true;
        // 鼠标释放已打开时不再重复弹出；键盘菜单键及触摸长按仍走 ContextRequested。
        if (_itemMenu?.IsOpen == true) return;
        ShowViewerItemMenu(e.TryGetPosition(Viewer, out var point) ? point : null);
    }

    private void Viewer_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewerState.IsOpen || ViewerState.CurrentItem is null
            || e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse
            || e.GetCurrentPoint(Viewer).Properties.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased) return;
        // 视频控件内部可能处理 ContextRequested；在画面层捕获右键释放，包含四周留白。
        e.Handled = true;
        ShowViewerItemMenu(e.GetCurrentPoint(Viewer).Position);
    }

    private void ShowViewerItemMenu(Point? point)
    {
        if (ViewerState.CurrentItem is not { } item) return;
        ShowItemMenu(Viewer, point, item.Path, false, singleTarget: true);
    }

    private void Gallery_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (_gesturePointer is not null || GalleryState.CurrentFolder is not { } path) return;
        bool positioned = e.TryGetPosition(GallerySurface, out var point);
        e.Handled = true;
        if (positioned && Environment.TickCount64 - _lastRightRelease < 500) return;
        ShowItemMenu(GallerySurface, positioned ? point : null, path, true, allowDelete: false);
    }

    private void ShowItemMenu(FrameworkElement target, Point? point, string path, bool folder, TreeViewNode? node = null, bool allowDelete = true, bool singleTarget = false)
    {
        StopHoverPreview();
        CloseItemMenu();
        if (node is not null) FolderTree.SelectedNode = node;
        else if (!singleTarget && GalleryState.Items.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)) is { } selected)
        {
            if (!_selectedPaths.Contains(selected.Path)) SelectItem(selected);
        }
        var menu = BuildItemMenu(path, folder, node, allowDelete, singleTarget);
        _itemMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_itemMenu, menu)) _itemMenu = null; };
        menu.ShowAt(target, new FlyoutShowOptions { Position = point });
    }

    private MenuFlyout BuildItemMenu(string path, bool folder, TreeViewNode? node = null, bool allowDelete = true, bool singleTarget = false)
    {
        var menu = new MenuFlyout();
        var targets = !singleTarget && node is null && allowDelete ? ContextFileTargets(path, folder) : [(path, folder)];
        bool multiple = targets.Length > 1;
        void Add(string text, string key, Func<Task> action)
        {
            string? icon = key switch
            {
                "open" => folder ? "IconFolder" : "IconOpen",
                "new-window" => "IconNewWindow",
                "expand" => node?.IsExpanded == true ? "IconCollapse" : "IconExpand",
                "reveal" => "IconOpenFolder",
                "copy-path" => "IconCopy",
                "copy-file" => "IconCopyFile",
                "paste" => "IconPaste",
                "new-folder" => "IconNewFolder",
                "identical-images" => "IconSearch",
                "cut" or "rename" => null,
                "delete" => "IconDelete",
                "exclude" => "IconFilter",
                _ => "IconRefresh",
            };
            var entry = new MenuFlyoutItem { Text = text, Tag = key, Icon = icon is null ? null : FluentIcons.Create(icon) };
            string textLanguage = AppText.Language;
            LiveText.Set(entry, "Text", localized => localized.Text = AppText.TranslateStatus(text, textLanguage));
            entry.Click += async (_, _) =>
            {
                try { await action(); }
                catch (Exception ex) { if (!_closed) GalleryState.Status = AppText.Format("OperationFailed", ex.Message); }
            };
            menu.Items.Add(entry);
        }
        if (node is null && !multiple) Add(folder ? AppText.Get("OpenFolder") : AppText.Get("Open"), "open", async () =>
        {
            if (folder) await NavigateAsync(path);
            else
            {
                int index = GalleryState.MediaItems.FindIndex(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) await ShowInViewerAsync(index);
            }
        });
        bool openFolderInWindow = folder && !multiple && (node is not null || allowDelete);
        string? windowPath = openFolderInWindow ? path : GalleryState.CurrentFolder
            ?? (folder ? path : System.IO.Path.GetDirectoryName(path));
        Add(openFolderInWindow ? AppText.Get("OpenInNewWindow") : AppText.Get("NewWindow"), "new-window",
            () => { RequestNewWindow(windowPath); return Task.CompletedTask; });
        if (!openFolderInWindow) ((MenuFlyoutItem)menu.Items[^1]).KeyboardAcceleratorTextOverride = "Ctrl+N";
        if (node is not null)
        {
            Add(AppText.Get("ExcludeThisFolder"), "exclude", () => { ExcludeTreeFolder(path); return Task.CompletedTask; });
            Add(node.IsExpanded ? AppText.Get("Collapse") : AppText.Get("Expand"), "expand", async () =>
            {
                if (node.IsExpanded) node.IsExpanded = false;
                else { await EnsureChildrenLoadedAsync(node); node.IsExpanded = true; }
            });
        }
        if (!multiple) Add(AppText.Get("ShowInFileExplorer"), "reveal", () => RevealPathAsync(path, folder));
        if (node is null && allowDelete && CanFindIdenticalImages(targets))
            Add(AppText.Get("FindAndRemoveIdenticalImages"), "identical-images", () => FindIdenticalImagesAsync(targets));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(AppText.Get("CopyFullPath"), "copy-path", () =>
        {
            var data = new DataPackage();
            data.SetText(string.Join(Environment.NewLine, targets.Select(target => target.Path)));
            Clipboard.SetContent(data);
            Clipboard.Flush();
            GalleryState.Status = AppText.Get("FullPathCopied");
            return Task.CompletedTask;
        });
        Add(multiple ? AppText.Format("CopyItems", targets.Length) : folder ? AppText.Get("CopyFolder") : AppText.Get("CopyFile"), "copy-file", () => CopyFilesAsync(targets));
        ((MenuFlyoutItem)menu.Items[^1]).KeyboardAcceleratorTextOverride = "Ctrl+C";
        if (allowDelete && System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path)) is not null)
        {
            Add(multiple ? AppText.Format("CutItems", targets.Length) : AppText.Get("Cut"), "cut", () => CopyFilesAsync(targets, true));
            ((MenuFlyoutItem)menu.Items[^1]).KeyboardAcceleratorTextOverride = "Ctrl+X";
            if (!multiple && (!ViewerState.IsOpen || IsFilmstrip))
            {
                Add(AppText.Get("Rename"), "rename", () => RenameItemAsync(path, folder));
                ((MenuFlyoutItem)menu.Items[^1]).KeyboardAcceleratorTextOverride = "F2";
            }
        }
        if (folder && !multiple)
        {
            Add(AppText.Get("NewFolder"), "new-folder", () => ShowNewFolderDialogAsync(path));
            if (string.Equals(path, GalleryState.CurrentFolder, StringComparison.OrdinalIgnoreCase))
                ((MenuFlyoutItem)menu.Items[^1]).KeyboardAcceleratorTextOverride = "Ctrl+Shift+N";
        }
        string? destination = multiple ? GalleryState.CurrentFolder : folder ? path : System.IO.Path.GetDirectoryName(path);
        Add(AppText.Get("Paste"), "paste", () => destination is null ? Task.CompletedTask : PasteFilesAsync(destination));
        ((MenuFlyoutItem)menu.Items[^1]).KeyboardAcceleratorTextOverride = "Ctrl+V";
        ((MenuFlyoutItem)menu.Items[^1]).IsEnabled = destination is not null && !_pasting && ClipboardHasFiles();
        if (folder && !multiple)
            Add(AppText.Get("RefreshFolder"), "refresh", async () =>
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
            Add(multiple ? AppText.Format("DeleteItems", targets.Length) : AppText.Get("Delete"), "delete", () => DeleteSelectionWithConfirmationAsync(targets, IsShiftDown()));
            ((MenuFlyoutItem)menu.Items[^1]).KeyboardAcceleratorTextOverride = "Delete";
        }
        return menu;
    }

    private static async Task RevealPathAsync(string path, bool folder)
    {
        string? parent = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path));
        if (parent is null)
        {
            if (!await Launcher.LaunchFolderPathAsync(path)) throw new IOException(AppText.Get("CouldNotOpenFileExplorer"));
            return;
        }
        var directory = await StorageFolder.GetFolderFromPathAsync(parent);
        IStorageItem item = folder ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path);
        var options = new FolderLauncherOptions();
        options.ItemsToSelect.Add(item);
        if (!await Launcher.LaunchFolderAsync(directory, options)) throw new IOException(AppText.Get("CouldNotOpenFileExplorer"));
    }
}
