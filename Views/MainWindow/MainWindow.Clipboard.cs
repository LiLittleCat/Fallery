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

    private static bool ClipboardHasFiles() => FileClipboard.HasFiles();

    private static bool IsClipboardTextTarget(DependencyObject? source)
    {
        for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is TextBox or RichEditBox or PasswordBox or ComboBox or ContentDialog
                or TextBlock { IsTextSelectionEnabled: true } or RichTextBlock { IsTextSelectionEnabled: true }) return true;
        return false;
    }

    private (string Path, bool Folder)[] ClipboardSelection(DependencyObject? source)
    {
        for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is TreeView tree && (tree == FolderTree || tree == FavoritesTree))
                return tree.SelectedNode?.Content is FolderEntry entry && !string.IsNullOrEmpty(entry.Path)
                    ? [(entry.Path, true)] : [];
        }
        if (ViewerState.IsOpen && !IsFilmstrip)
            return ViewerState.CurrentItem is { } item ? [(item.Path, item.IsFolder)] : [];
        return SelectedFileTargets();
    }

    private Task CopyFileAsync(string path, bool folder) => CopyFilesAsync([(path, folder)]);

    private async Task CopyFilesAsync(IReadOnlyList<(string Path, bool Folder)> targets, bool cut = false)
    {
        if (targets.Count == 0) return;
        try
        {
            var items = new List<IStorageItem>(targets.Count);
            foreach (var target in targets)
                items.Add(target.Folder ? await StorageFolder.GetFolderFromPathAsync(target.Path) : await StorageFile.GetFileFromPathAsync(target.Path));
            if (cut && targets.Any(target => System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(target.Path)) is null))
                throw new IOException(AppText.Get("CannotCutADriveRoot"));
            var data = new DataPackage { RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy };
            data.SetStorageItems(items);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            GalleryState.Status = AppText.Format("ItemsPressCtrlVInTheDestinationFolderTo", (cut ? AppText.Get("Cut") : AppText.Get("Copy")), items.Count);
        }
        catch (Exception ex) { if (!_closed) GalleryState.Status = AppText.Format("Failed", (cut ? AppText.Get("Cut") : AppText.Get("Copy")), ex.Message); }
    }

    private async Task HandleFileClipboardKeyAsync(VirtualKey key, DependencyObject? source)
    {
        if (key is VirtualKey.C or VirtualKey.X)
            await CopyFilesAsync(ClipboardSelection(source), key == VirtualKey.X);
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
            var content = await FileClipboard.ReadAsync(WinRT.Interop.WindowNative.GetWindowHandle(this), _lifetimeCts.Token);
            if (content is null || content.Paths.Count == 0)
            { GalleryState.Status = AppText.Get("TheClipboardContainsNoFilesOrFoldersToPaste"); return; }
            var paths = content.Paths;
            GalleryState.Status = AppText.Format("Items", (content.Move ? AppText.Get("Move") : AppText.Get("Copy")), paths.Count);
            bool move = content.Move;
            StopHoverPreview();
            if (ViewerState.IsOpen) CloseViewer();
            var result = await FilePaste.TransferAsync(paths, destination, move, ResolveFileConflictAsync, _lifetimeCts.Token);
            if (_closed || _closing) return;
            FileClipboard.CompleteMove(content, result, WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (result.Copied.Count > 0) await RefreshAndSelectFilesAsync(result.Copied, destination);
            GalleryState.Status = AppText.Format("Items724e38", (result.Canceled ? AppText.Get("PasteCanceledProcessed") : AppText.Get("Pasted")), result.Copied.Count)
                + (result.Skipped > 0 ? AppText.Format("SkippedItems", result.Skipped) : "")
                + (result.Errors.Count > 0 ? AppText.Format("ItemsIncomplete", result.Errors.Count, result.Errors[0]) : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) GalleryState.Status = AppText.Format("PasteFailed", ex.Message); }
        finally
        {
            _pasting = false;
            if (!_closed && !_closing && GalleryState.CurrentFolder is { } current) QueueFolderChange(current);
        }
    }
}
