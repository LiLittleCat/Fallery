using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Fallery;

public sealed partial class MainWindow
{
    private Pointer? _marqueePointer;
    private bool _marqueeStarted, _marqueeActive, _marqueeControl, _marqueeShift, _suppressMarqueeTap;
    private Point _marqueeOrigin, _marqueeViewportPoint;
    private HashSet<string> _marqueeOriginal = new(StringComparer.OrdinalIgnoreCase);
    private string? _marqueeOriginalFocus, _marqueeOriginalAnchor, _marqueeOriginalCursor;
    private readonly DispatcherTimer _marqueeScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    private void InitializeMarqueeSelection()
    {
        _marqueeScrollTimer.Tick += (_, _) => ScrollMarqueeAtEdge();
        GalleryScroller.ViewChanged += (_, _) => UpdateMarqueeFromViewport();
        GalleryScroller.SizeChanged += (_, _) => { if (_marqueeStarted) EndMarqueeSelection(cancel: true); };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) EndMarqueeSelection(cancel: true);
        };
    }

    private bool TryStartMarquee(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(GalleryScroller);
        if (!point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed
            || IsGallerySelectionBlockedByDelete || _pasting || _settingsDialog is not null || _searchDialog is not null
            || _itemMenu is not null || _addressFlyout is not null || _gesturePointer is not null
            || !IsGalleryInput(e.OriginalSource as DependencyObject)
            || FindMediaItem(e.OriginalSource as DependencyObject) is not null) return false;
        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
            if (source is ScrollBar or ButtonBase || source == Viewer) return false;
        if (!GallerySurface.CapturePointer(e.Pointer)) return false;
        BeginMarqueeSelection(e.GetCurrentPoint(Gallery).Position, IsControlDown(), IsShiftDown());
        _marqueePointer = e.Pointer;
        _marqueeViewportPoint = point.Position;
        GalleryScroller.Focus(FocusState.Pointer);
        e.Handled = true;
        return true;
    }

    private void BeginMarqueeSelection(Point origin, bool control, bool shift)
    {
        CancelBrowsePositionRestore();
        StopHoverPreview();
        _marqueeOriginal = new(_selectedPaths, StringComparer.OrdinalIgnoreCase);
        _marqueeOriginalFocus = _selectedItem?.Path;
        _marqueeOriginalAnchor = _selectionAnchor;
        _marqueeOriginalCursor = _selectionCursor;
        _marqueeOrigin = origin;
        _marqueeControl = control;
        _marqueeShift = shift;
        _marqueeStarted = true;
        _marqueeActive = false;
    }

    private void MoveMarquee(PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(GalleryScroller).Properties.IsLeftButtonPressed)
        { EndMarqueeSelection(cancel: true); return; }
        _marqueeViewportPoint = e.GetCurrentPoint(GalleryScroller).Position;
        UpdateMarqueeFromViewport();
        if (_marqueeActive) _marqueeScrollTimer.Start();
        e.Handled = true;
    }

    private void UpdateMarqueeFromViewport()
    {
        if (!_marqueeStarted) return;
        var point = new Point(Math.Clamp(_marqueeViewportPoint.X, 0, GalleryScroller.ViewportWidth),
            Math.Clamp(_marqueeViewportPoint.Y, 0, GalleryScroller.ViewportHeight));
        UpdateMarqueeSelection(GalleryScroller.TransformToVisual(Gallery).TransformPoint(point));
    }

    private void UpdateMarqueeSelection(Point current)
    {
        if (!_marqueeStarted) return;
        if (!_marqueeActive && Math.Abs(current.X - _marqueeOrigin.X) < 4 && Math.Abs(current.Y - _marqueeOrigin.Y) < 4) return;
        _marqueeActive = true;
        _suppressMarqueeTap = true;
        var rect = new Rect(Math.Min(current.X, _marqueeOrigin.X), Math.Min(current.Y, _marqueeOrigin.Y),
            Math.Abs(current.X - _marqueeOrigin.X), Math.Abs(current.Y - _marqueeOrigin.Y));
        var selected = _marqueeControl || _marqueeShift
            ? new HashSet<string>(_marqueeOriginal, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (int index in MarqueeHitIndices(rect))
        {
            string path = GalleryState.Items[index].Path;
            if (_marqueeControl && !_marqueeShift && _marqueeOriginal.Contains(path)) selected.Remove(path);
            else selected.Add(path);
        }
        if (!_selectedPaths.SetEquals(selected))
        {
            _selectedPaths.Clear(); _selectedPaths.UnionWith(selected);
            if (_selectedItem is null || !_selectedPaths.Contains(_selectedItem.Path))
                _selectedItem = GalleryState.Items.FirstOrDefault(item => _selectedPaths.Contains(item.Path));
            _selectionAnchor = _marqueeControl || _marqueeShift ? _marqueeOriginalAnchor ?? _selectedItem?.Path : _selectedItem?.Path;
            _selectionCursor = _selectedItem?.Path;
            RefreshSelectionVisuals();
        }
        // 命中使用内容坐标，选框绘制使用视口坐标；滚动时起点仍固定在原来的内容位置。
        MarqueeOverlay.Visibility = Visibility.Visible;
        var visible = Gallery.TransformToVisual(GalleryScroller).TransformBounds(rect);
        double left = Math.Clamp(visible.Left, 0, GalleryScroller.ViewportWidth);
        double top = Math.Clamp(visible.Top, 0, GalleryScroller.ViewportHeight);
        double right = Math.Clamp(visible.Right, left, GalleryScroller.ViewportWidth);
        double bottom = Math.Clamp(visible.Bottom, top, GalleryScroller.ViewportHeight);
        Canvas.SetLeft(SelectionMarquee, left); Canvas.SetTop(SelectionMarquee, top);
        SelectionMarquee.Width = right - left; SelectionMarquee.Height = bottom - top;
    }

    private IEnumerable<int> MarqueeHitIndices(Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) yield break;
        if (!IsFilmstrip)
        {
            int firstHit = -1, lastHit = -1;
            foreach (int index in Masonry.IntersectingIndices(rect.X, rect.Y, rect.Width, rect.Height))
            {
                if (index >= GalleryState.Items.Count) continue;
                if (firstHit < 0) firstHit = index;
                lastHit = index;
            }
            // 跨行时按当前排列顺序补齐两端之间的文件，不能只留下矩形覆盖的几列。
            // 同行命中本来就是连续区间；没有命中任何卡片时保持空选区。
            for (int index = firstHit; index >= 0 && index <= lastHit; index++) yield return index;
            yield break;
        }
        double height = GalleryState.ShowFileNames ? 132 : 110;
        if (rect.Bottom <= 0 || rect.Top >= height) yield break;
        int first = Math.Max(0, (int)Math.Floor(rect.Left / 128));
        int end = Math.Min(GalleryState.Items.Count, (int)Math.Ceiling(rect.Right / 128));
        for (int index = first; index < end; index++)
            if (index * 128 + FilmstripTileWidth > rect.Left && index * 128 < rect.Right) yield return index;
    }

    private static double MarqueeScrollDelta(double point, double extent)
    {
        const double edge = 28;
        if (point < edge) return -Math.Clamp((edge - point) * 0.7, 0, 28);
        if (point > extent - edge) return Math.Clamp((point - extent + edge) * 0.7, 0, 28);
        return 0;
    }

    private void ScrollMarqueeAtEdge()
    {
        if (!_marqueeActive) return;
        if (IsFilmstrip)
            GalleryScroller.ChangeView(Math.Clamp(GalleryScroller.HorizontalOffset
                + MarqueeScrollDelta(_marqueeViewportPoint.X, GalleryScroller.ViewportWidth), 0, GalleryScroller.ScrollableWidth), null, null, true);
        else
            GalleryScroller.ChangeView(null, Math.Clamp(GalleryScroller.VerticalOffset
                + MarqueeScrollDelta(_marqueeViewportPoint.Y, GalleryScroller.ViewportHeight), 0, GalleryScroller.ScrollableHeight), null, true);
        UpdateMarqueeFromViewport();
    }

    private void EndMarqueeSelection(bool cancel)
    {
        if (!_marqueeStarted) return;
        bool dragged = _marqueeActive;
        _marqueeStarted = _marqueeActive = false;
        if (cancel) _suppressMarqueeTap = true;
        _marqueeScrollTimer.Stop();
        MarqueeOverlay.Visibility = Visibility.Collapsed;
        var pointer = _marqueePointer; _marqueePointer = null;
        if (pointer is not null) GallerySurface.ReleasePointerCapture(pointer);
        if (cancel && !_closed)
        {
            RestoreItemSelection(_marqueeOriginal, _marqueeOriginalFocus, _marqueeOriginalAnchor);
            _selectionCursor = _marqueeOriginalCursor;
        }
        else if (!dragged && !_marqueeControl && !_marqueeShift && !_closed) SelectItem(null);
        _marqueeOriginal.Clear();
    }
}
