using Fallery.Controls;
using Fallery.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Fallery;

public sealed partial class MainWindow
{
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectionAnchor;
    private string? _selectionCursor;

    private bool IsGalleryInput(DependencyObject? source)
    {
        for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element == GalleryScroller) return true;
        return false;
    }

    private void GalleryBlank_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_marqueeStarted || _suppressMarqueeTap) { e.Handled = true; return; }
        if (IsGallerySelectionBlockedByDelete || _pasting || GalleryState.IsScanning || IsControlDown() || IsShiftDown()
            || FindMediaItem(e.OriginalSource as DependencyObject) is not null) return;
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is ScrollBar || element == Viewer) return;
        CancelBrowsePositionRestore();
        SelectItem(null);
        GalleryScroller.Focus(FocusState.Pointer);
    }

    private void SelectAllItems()
    {
        CancelBrowsePositionRestore();
        foreach (var item in GalleryState.Items) _selectedPaths.Add(item.Path);
        _selectedItem ??= GalleryState.Items.FirstOrDefault();
        _selectionAnchor ??= _selectedItem?.Path;
        _selectionCursor ??= _selectedItem?.Path;
        RefreshSelectionVisuals();
    }

    private int FindKeyboardNeighbor(int current, VirtualKey key)
    {
        int count = GalleryState.Items.Count;
        if (count == 0) return -1;
        if (key == VirtualKey.Home || current < 0) return 0;
        if (key == VirtualKey.End) return count - 1;
        if (IsFilmstrip) return Math.Clamp(current + (key is VirtualKey.Left or VirtualKey.Up ? -1 : 1), 0, count - 1);
        if (Masonry.BoundsOf(current) is not { } origin) return current;
        double x = origin.X + origin.Width / 2, y = origin.Y + origin.Height / 2;
        int best = current;
        double score = double.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (i == current || Masonry.BoundsOf(i) is not { } candidate) continue;
            double dx = candidate.X + candidate.Width / 2 - x, dy = candidate.Y + candidate.Height / 2 - y;
            bool horizontal = key is VirtualKey.Left or VirtualKey.Right;
            double along = horizontal ? dx : dy, across = horizontal ? dy : dx;
            if (key is VirtualKey.Left or VirtualKey.Up) along = -along;
            if (along <= 1) continue;
            // 优先沿同一行/列移动，只查询纯几何缓存，不实现屏幕外控件。
            bool overlaps = horizontal
                ? candidate.Y < origin.Bottom && candidate.Bottom > origin.Y
                : candidate.X < origin.X + origin.Width && candidate.X + candidate.Width > origin.X;
            if (horizontal && !overlaps) continue;
            double distance = along + Math.Abs(across) * 2 + (overlaps ? 0 : 1000000);
            if (distance < score) { score = distance; best = i; }
        }
        return best == current && key is VirtualKey.Left or VirtualKey.Right
            ? Math.Clamp(current + (key == VirtualKey.Left ? -1 : 1), 0, count - 1) : best;
    }

    private void MoveKeyboardSelection(VirtualKey key, bool control, bool shift)
    {
        CancelBrowsePositionRestore();
        int current = GalleryState.Items.FindIndex(item => string.Equals(item.Path, _selectionCursor ?? _selectedItem?.Path, StringComparison.OrdinalIgnoreCase));
        int next = FindKeyboardNeighbor(current, key);
        if (next < 0) return;
        var item = GalleryState.Items[next];
        if (!control || shift) ApplyItemSelection(item, control, shift);
        FocusGalleryItem(item);
    }

    private async Task<bool> HandleExplorerKeyAsync(KeyRoutedEventArgs e, bool control, bool alt)
    {
        var key = e.Key;
        var source = e.OriginalSource as DependencyObject;
        if (alt || _settingsDialog is not null || _itemMenu is not null || _addressFlyout is not null
            || IsGallerySelectionBlockedByDelete || _pasting || _pickingFolder || IsClipboardTextTarget(source)
            || (ViewerState.IsOpen && !IsFilmstrip)) return false;
        if (!control && !IsShiftDown() && FindInputTree(source) is { } tree
            && key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right or VirtualKey.Enter or VirtualKey.Space)
        {
            e.Handled = true;
            var node = FocusedTreeNode(tree, source);
            if (key is VirtualKey.Right or VirtualKey.Enter or VirtualKey.Space) await EnterGalleryFromTreeAsync(tree, node);
            else MoveTreeSelection(tree, node, key);
            return true;
        }
        if (GalleryState.IsScanning) return false;
        if (control && IsShiftDown() && key == VirtualKey.N)
        { NewFolder_Click(this, new RoutedEventArgs()); return true; }
        if (key == VirtualKey.F2)
        {
            e.Handled = true;
            var targets = ClipboardSelection(source);
            if (targets.Length == 1) await RenameItemAsync(targets[0].Path, targets[0].Folder);
            return true;
        }
        if (!IsGalleryInput(source)) return false;
        if (FindMediaItem(source) is { } focusedItem) _selectionCursor = focusedItem.Path;
        if (control && key == VirtualKey.A) { SelectAllItems(); return true; }
        if (key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End)
        {
            e.Handled = true;
            MoveKeyboardSelection(key, control, IsShiftDown());
            if (IsFilmstrip && !control && !IsShiftDown() && _selectedItem is { IsFolder: false } preview)
                await ShowInViewerAsync(GalleryState.MediaItems.IndexOf(preview));
            return true;
        }
        if (control && key == VirtualKey.Space)
        {
            var item = GalleryState.Items.FirstOrDefault(item => string.Equals(item.Path, _selectionCursor, StringComparison.OrdinalIgnoreCase));
            if (item is not null) ApplyItemSelection(item, true, false);
            return true;
        }
        if (!control && !IsShiftDown() && key is VirtualKey.Enter or VirtualKey.Space)
        {
            e.Handled = true;
            await OpenKeyboardSelectionAsync();
            return true;
        }
        if (!control && key == VirtualKey.Escape && _presenterBeforeFullScreen is null && _gesturePointer is null && !GalleryState.IsSearching)
        { e.Handled = true; await ReturnToFolderTreeAsync(); return true; }
        if (!control && key == VirtualKey.Back) { e.Handled = true; await GoBackAsync(); return true; }
        return false;
    }

    private static bool IsControlDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;

    private void ApplyItemSelection(MediaItem item, bool control, bool shift)
    {
        int index = GalleryState.Items.IndexOf(item);
        if (index < 0 || IsGallerySelectionBlockedByDelete) return;
        CancelBrowsePositionRestore();
        _selectionCursor = item.Path;
        int anchor = GalleryState.Items.FindIndex(candidate => string.Equals(candidate.Path, _selectionAnchor, StringComparison.OrdinalIgnoreCase));
        if (shift)
        {
            if (anchor < 0) { anchor = index; _selectionAnchor = item.Path; }
            if (!control) _selectedPaths.Clear();
            for (int i = Math.Min(anchor, index); i <= Math.Max(anchor, index); i++)
                _selectedPaths.Add(GalleryState.Items[i].Path);
        }
        else
        {
            if (!control) _selectedPaths.Clear();
            if (!control || !_selectedPaths.Remove(item.Path)) _selectedPaths.Add(item.Path);
            _selectionAnchor = item.Path;
        }
        _selectedItem = _selectedPaths.Contains(item.Path) ? item
            : GalleryState.Items.FirstOrDefault(candidate => _selectedPaths.Contains(candidate.Path));
        RefreshSelectionVisuals();
    }

    private void RestoreItemSelection(IEnumerable<string> paths, string? focused, string? anchor)
    {
        var valid = GalleryState.Items.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        _selectedPaths.Clear();
        foreach (string path in paths) if (valid.ContainsKey(path)) _selectedPaths.Add(path);
        _selectedItem = focused is not null && _selectedPaths.Contains(focused) && valid.TryGetValue(focused, out var item)
            ? item : GalleryState.Items.FirstOrDefault(candidate => _selectedPaths.Contains(candidate.Path));
        _selectionAnchor = anchor is not null && valid.ContainsKey(anchor) ? anchor : _selectedItem?.Path;
        _selectionCursor = _selectedItem?.Path;
        RefreshSelectionVisuals();
    }

    private void RefreshSelectionVisuals()
    {
        if (_closed) return;
        foreach (var tile in _realizedItems.Keys.OfType<MediaTile>()) ConfigureTile(tile);
        SelectionCountText.Text = _selectedPaths.Count > 1 ? $"已选择 {_selectedPaths.Count:N0} 项" : "";
    }

    private (string Path, bool Folder)[] SelectedFileTargets() => GalleryState.Items
        .Where(item => _selectedPaths.Contains(item.Path)).Select(item => (item.Path, item.IsFolder)).ToArray();

    private (string Path, bool Folder)[] ContextFileTargets(string path, bool folder) =>
        (!ViewerState.IsOpen || IsFilmstrip) && _selectedPaths.Contains(path)
            ? SelectedFileTargets() : [(path, folder)];
}
