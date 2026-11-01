using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Fallery;

public sealed partial class MainWindow
{
    // 只记路径与位置，不延长媒体模型、缩略图或控件的生命周期。
    private sealed record BrowsePosition(string? Selected, string? Anchor, double AnchorOffset,
        double Horizontal, double Vertical, GalleryDisplayMode Mode)
    {
        public string[] SelectedPaths { get; init; } = [];
        public string? SelectionAnchor { get; init; }
    }
    private readonly BoundedCache<string, BrowsePosition> _browsePositions = new(20_000, 128);
    private (string Folder, BrowsePosition Position)? _pendingBrowsePosition;
    private int _browsePositionVersion;
    private string? _displayedBrowseFolder;

    private void InitializeBrowsePositions()
    {
        GalleryScroller.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler((_, _) => CancelBrowsePositionRestore()), true);
        GalleryScroller.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => CancelBrowsePositionRestore()), true);
        GalleryScroller.KeyDown += (_, _) => CancelBrowsePositionRestore();
        GalleryScroller.ViewChanged += (_, e) => { if (e.IsIntermediate) CancelBrowsePositionRestore(); };
    }

    private void CancelBrowsePositionRestore()
    {
        _browsePositionVersion++;
        _pendingBrowsePosition = null;
    }

    private void RememberBrowsePosition()
    {
        if (_closed || GalleryState.IsScanning || ViewerState.IsOpen && !IsFilmstrip
            || GalleryState.CurrentFolder is not { } folder || _displayedBrowseFolder != folder) return;
        string? anchor = null;
        double anchorOffset = 0;
        if (IsFilmstrip)
        {
            int index = (int)(GalleryScroller.HorizontalOffset / 128);
            if (index >= 0 && index < GalleryState.Items.Count)
            {
                anchor = GalleryState.Items[index].Path;
                anchorOffset = index * 128 - GalleryScroller.HorizontalOffset;
            }
        }
        else
        {
            double top = GalleryScroller.VerticalOffset, bottom = top + GalleryScroller.ViewportHeight;
            double firstY = double.PositiveInfinity;
            foreach (var (element, item) in _realizedItems)
            {
                if (Masonry.BoundsOf(Gallery.GetElementIndex(element)) is not { } bounds
                    || bounds.Bottom <= top || bounds.Y >= bottom) continue;
                if (ReferenceEquals(item, _selectedItem))
                {
                    anchor = item.Path;
                    anchorOffset = bounds.Y - top;
                    break;
                }
                if (bounds.Y >= firstY) continue;
                firstY = bounds.Y;
                anchor = item.Path;
                anchorOffset = bounds.Y - top;
            }
        }
        _browsePositions.Set(folder.ToUpperInvariant(), new(_selectedItem?.Path, anchor, anchorOffset,
            GalleryScroller.HorizontalOffset, GalleryScroller.VerticalOffset, GalleryState.DisplayMode)
            { SelectedPaths = _selectedPaths.ToArray(), SelectionAnchor = _selectionAnchor }, Math.Max(1, _selectedPaths.Count));
    }

    private void PrepareBrowseNavigation(string destination)
    {
        CancelBrowsePositionRestore();
        string target = NormalizeFolderPath(destination);
        // 从树、地址栏或右键进入子目录，也把父目录中的入口标为选中。
        if (!GalleryState.IsScanning && !ViewerState.IsOpen)
        {
            var child = GalleryState.Items.FirstOrDefault(item => item.IsFolder
                && string.Equals(item.Path, target, StringComparison.OrdinalIgnoreCase));
            if (child is not null) SelectItem(child);
        }
        RememberBrowsePosition();
        // 上级/祖先跳转时，返回的是刚离开的子目录；即使父目录没有缓存也应定位它。
        if (GalleryState.CurrentFolder is not { } current || string.Equals(current, target, StringComparison.OrdinalIgnoreCase)) return;
        string? childPath = current;
        while (childPath is not null && System.IO.Path.GetDirectoryName(childPath) is { } parent)
        {
            if (string.Equals(parent, target, StringComparison.OrdinalIgnoreCase))
            {
                _browsePositions.TryGet(target.ToUpperInvariant(), out var saved);
                saved ??= new(null, null, 0, 0, 0, GalleryState.DisplayMode);
                _browsePositions.Set(target.ToUpperInvariant(), saved with { Selected = childPath, SelectedPaths = [childPath], SelectionAnchor = childPath });
                break;
            }
            childPath = parent;
        }
    }

    private void RestoreBrowsePosition(string? selectedOverride = null)
    {
        if (_closed || GalleryState.CurrentFolder is not { } folder) return;
        _browsePositions.TryGet(folder.ToUpperInvariant(), out var saved);
        saved ??= new(null, null, 0, 0, 0, GalleryState.DisplayMode);
        if (selectedOverride is not null) saved = saved with
        {
            Selected = selectedOverride,
            SelectedPaths = saved.SelectedPaths.Contains(selectedOverride, StringComparer.OrdinalIgnoreCase) ? saved.SelectedPaths : [selectedOverride]
        };
        RestoreItemSelection(saved.SelectedPaths.Length > 0 ? saved.SelectedPaths : saved.Selected is { } selected ? [selected] : [],
            saved.Selected, saved.SelectionAnchor);
        _pendingBrowsePosition = (folder, saved);
        QueueBrowsePositionRestore();
    }

    private void QueueBrowsePositionRestore()
    {
        if (_pendingBrowsePosition is not { } pending) return;
        int request = ++_browsePositionVersion;
        int navigation = _navigationVersion;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_closed || request != _browsePositionVersion || navigation != _navigationVersion
                || GalleryState.IsScanning || ViewerState.IsOpen && !IsFilmstrip
                || !string.Equals(GalleryState.CurrentFolder, pending.Folder, StringComparison.OrdinalIgnoreCase)) return;
            // 在查看器关闭、窗口还原和新 ItemsSource 完成布局后，再按路径定位；不能用旧几何。
            Root.UpdateLayout();
            var saved = pending.Position;
            bool sameMode = saved.Mode == GalleryState.DisplayMode;
            double horizontal = sameMode ? saved.Horizontal : 0, vertical = sameMode ? saved.Vertical : 0;
            int anchor = GalleryState.Items.FindIndex(item => string.Equals(item.Path, saved.Anchor, StringComparison.OrdinalIgnoreCase));
            if (sameMode && anchor >= 0)
            {
                if (IsFilmstrip) horizontal = anchor * 128 - saved.AnchorOffset;
                else if (Masonry.BoundsOf(anchor) is { } bounds) vertical = bounds.Y - saved.AnchorOffset;
            }
            int selected = GalleryState.Items.FindIndex(item => string.Equals(item.Path, saved.Selected, StringComparison.OrdinalIgnoreCase));
            if (selected >= 0)
            {
                if (IsFilmstrip)
                {
                    double left = selected * 128;
                    if (left < horizontal || left + FilmstripTileWidth > horizontal + GalleryScroller.ViewportWidth)
                        horizontal = left - (GalleryScroller.ViewportWidth - FilmstripTileWidth) / 2;
                }
                else if (Masonry.BoundsOf(selected) is { } bounds
                    && (bounds.Bottom <= vertical || bounds.Y >= vertical + GalleryScroller.ViewportHeight))
                    vertical = bounds.Y - Math.Max(0, (GalleryScroller.ViewportHeight - bounds.Height) / 2);
            }
            GalleryScroller.ChangeView(Math.Clamp(horizontal, 0, GalleryScroller.ScrollableWidth),
                Math.Clamp(vertical, 0, GalleryScroller.ScrollableHeight), null, true);
        });
    }
}
