using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace Fallery;

public sealed partial class MainWindow
{
    private bool _pasting;

    private static bool ClipboardHasFiles()
    {
        try { return Clipboard.GetContent().Contains(StandardDataFormats.StorageItems); }
        catch (Exception) { return false; }
    }

    private static bool IsClipboardTextTarget(DependencyObject? source)
    {
        for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is TextBox or RichEditBox or PasswordBox or ComboBox or ContentDialog
                or TextBlock { IsTextSelectionEnabled: true } or RichTextBlock { IsTextSelectionEnabled: true }) return true;
        return false;
    }

    private (string Path, bool Folder)? ClipboardSelection(DependencyObject? source)
    {
        for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is TreeView tree && (tree == FolderTree || tree == FavoritesTree))
                return tree.SelectedNode?.Content is FolderEntry entry && !string.IsNullOrEmpty(entry.Path)
                    ? (entry.Path, true) : null;
        }
        var item = ViewerState.IsOpen ? ViewerState.CurrentItem : _selectedItem;
        return item is null ? null : (item.Path, item.IsFolder);
    }

    private async Task CopyFileAsync(string path, bool folder)
    {
        try
        {
            IStorageItem item = folder ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path);
            var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            data.SetStorageItems(new[] { item });
            Clipboard.SetContent(data);
            Clipboard.Flush();
            GalleryState.Status = "已复制，可在目标文件夹按 Ctrl+V 粘贴";
        }
        catch (Exception ex) { if (!_closed) GalleryState.Status = $"复制失败：{ex.Message}"; }
    }

    private async Task HandleFileClipboardKeyAsync(VirtualKey key, DependencyObject? source)
    {
        if (key == VirtualKey.C && ClipboardSelection(source) is { } item)
            await CopyFileAsync(item.Path, item.Folder);
        else if (key == VirtualKey.V)
        {
            string? destination = GalleryState.CurrentFolder;
            for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
                if (element is TreeView tree && (tree == FolderTree || tree == FavoritesTree))
                {
                    destination = tree.SelectedNode?.Content is FolderEntry entry ? entry.Path : null;
                    break;
                }
            if (!string.IsNullOrEmpty(destination)) await PasteFilesAsync(destination);
        }
    }

    private async Task PasteFilesAsync(string destination)
    {
        if (_pasting || _deleting || _closed || _closing) return;
        _pasting = true;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.StorageItems))
            { GalleryState.Status = "剪贴板中没有可粘贴的文件或文件夹"; return; }
            var items = await content.GetStorageItemsAsync().AsTask(_lifetimeCts.Token);
            var paths = items.Select(item => item.Path).Where(path => !string.IsNullOrEmpty(path)).ToArray();
            if (paths.Length != items.Count) throw new IOException("部分剪贴板项目没有本地文件路径，请先在资源管理器中保存到磁盘。");
            GalleryState.Status = $"正在粘贴 {paths.Length} 项…";
            // 此入口只复制。即使来自外部应用的“剪切”，也不删除源文件。
            var result = await FilePaste.CopyAsync(paths, destination, _lifetimeCts.Token);
            if (_closed || _closing) return;
            GalleryState.Status = result.Errors.Count == 0 ? $"已粘贴 {result.Copied.Count} 项" :
                $"已粘贴 {result.Copied.Count} 项，{result.Errors.Count} 项未完成：{result.Errors[0]}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) GalleryState.Status = $"粘贴失败：{ex.Message}"; }
        finally
        {
            _pasting = false;
            if (!_closed && !_closing && GalleryState.CurrentFolder is { } current) QueueFolderChange(current);
        }
    }
}
