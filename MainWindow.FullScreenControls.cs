using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Fallery;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _fullScreenControlsTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _fullScreenControlsVisible;
    private bool _pointerOverFullScreenControls;
    private bool _fullScreenControlPressed;

    private void InitializeFullScreenControls()
    {
        Viewer.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(FullScreenControls_PointerMoved), true);
        Viewer.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(FullScreenControls_PointerPressed), true);
        Viewer.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(FullScreenControls_PointerReleased), true);
        Viewer.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(FullScreenControls_PointerReleased), true);
        VideoControls.PointerExited += FullScreenControls_PointerExited;
        ViewerBar.PointerExited += FullScreenControls_PointerExited;
        VideoControls.GotFocus += FullScreenControls_GotFocus;
        ViewerBar.GotFocus += FullScreenControls_GotFocus;
        foreach (var button in new[] { PrevButton, NextButton })
        {
            button.PointerEntered += (_, _) => button.Opacity = 1;
            button.PointerExited += (_, _) => button.Opacity = _presenterBeforeFullScreen is null ? 1 : 0;
            button.GotFocus += (_, _) => button.Opacity = 1;
            button.LostFocus += (_, _) => button.Opacity = _presenterBeforeFullScreen is null ? 1 : 0;
        }
        _fullScreenControlsTimer.Tick += (_, _) => HideFullScreenControlsIfIdle();
    }

    private void ResetFullScreenControls()
    {
        _fullScreenControlsTimer.Stop();
        _fullScreenControlsVisible = _pointerOverFullScreenControls = _fullScreenControlPressed = false;
        PrevButton.Opacity = NextButton.Opacity = _presenterBeforeFullScreen is null ? 1 : 0;
        ApplyFullScreenControls();
    }

    private void ApplyFullScreenControls()
    {
        bool fullScreen = _presenterBeforeFullScreen is not null;
        // 保留底栏的测量尺寸，但让全屏画面跨过它，显隐不会触发视频缩放。
        Grid.SetRowSpan(ViewerStage, fullScreen ? 3 : 1);
        FullScreenControlsHotspot.Visibility = fullScreen ? Visibility.Visible : Visibility.Collapsed;
        bool visible = !fullScreen || _fullScreenControlsVisible;
        VideoControls.Opacity = ViewerBar.Opacity = visible ? 1 : 0;
        VideoControls.IsHitTestVisible = ViewerBar.IsHitTestVisible = visible;
    }

    private void ShowFullScreenControls()
    {
        if (_presenterBeforeFullScreen is null) return;
        _fullScreenControlsTimer.Stop();
        _fullScreenControlsVisible = true;
        ApplyFullScreenControls();
    }

    private void ScheduleFullScreenControlsHide()
    {
        if (_presenterBeforeFullScreen is not null && !_fullScreenControlsTimer.IsEnabled)
            _fullScreenControlsTimer.Start();
    }

    private void HideFullScreenControlsIfIdle()
    {
        if (_closed || _presenterBeforeFullScreen is null)
        {
            _fullScreenControlsTimer.Stop();
            return;
        }
        // 菜单位于独立 Popup 中，离开底栏进入菜单或拖动滑块时不能收起操作入口。
        if (_pointerOverFullScreenControls || _fullScreenControlPressed
            || (Root.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0)) return;
        _fullScreenControlsTimer.Stop();
        _fullScreenControlsVisible = false;
        ApplyFullScreenControls();
    }

    private void FullScreenControls_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_presenterBeforeFullScreen is null) return;
        var point = e.GetCurrentPoint(FullScreenControlsHotspot).Position;
        _pointerOverFullScreenControls = new Rect(0, 0, FullScreenControlsHotspot.ActualWidth,
            FullScreenControlsHotspot.ActualHeight).Contains(point);
        if (_pointerOverFullScreenControls) ShowFullScreenControls();
        else ScheduleFullScreenControlsHide();
    }

    private void FullScreenControls_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverFullScreenControls = false;
        ScheduleFullScreenControlsHide();
    }

    private void FullScreenControls_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_presenterBeforeFullScreen is not null && _pointerOverFullScreenControls)
            _fullScreenControlPressed = true;
    }

    private void FullScreenControls_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _fullScreenControlPressed = false;
        ScheduleFullScreenControlsHide();
    }

    private void FullScreenControls_GotFocus(object sender, RoutedEventArgs e)
    {
        ShowFullScreenControls();
        ScheduleFullScreenControlsHide();
    }
}
