using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Fallery;

public sealed partial class MainWindow
{
    private int _viewerWheelDelta;
    private long _viewerWheelTime;
    private long _viewerWheelSwitchTime = long.MinValue / 2;

    private bool CanNavigateViewerWheel(bool modified, bool horizontal) =>
        !modified && !horizontal && !_closed && !_closing && ViewerState.IsOpen && !GalleryState.IsScanning
        && _settingsDialog is null && !_deleting && !_pickingFolder && _panOrigin is null
        && AddressEditor.Visibility != Visibility.Visible && _addressFlyout is null && _itemMenu is null
        && (Root.XamlRoot is not { } root || !VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.Child is not ToolTip));

    private async void ViewerStage_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ViewerStage);
        bool modified = (e.KeyModifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift | VirtualKeyModifiers.Menu)) != 0;
        if (!CanNavigateViewerWheel(modified, point.Properties.IsHorizontalMouseWheel))
        {
            _viewerWheelDelta = 0;
            return;
        }
        // 画面内滚轮切换，拖动后的滚动条本身仍保留原有操作。
        for (var source = e.OriginalSource as DependencyObject; source is not null && source != ViewerStage; source = VisualTreeHelper.GetParent(source))
            if (source is ScrollBar) return;
        e.Handled = true;
        await NavigateViewerWheelAsync(point.Properties.MouseWheelDelta, Environment.TickCount64);
    }

    private async Task NavigateViewerWheelAsync(int delta, long timestamp)
    {
        if (!CanNavigateViewerWheel(false, false) || delta == 0) return;
        if (timestamp - _viewerWheelTime > 400 || Math.Sign(delta) != Math.Sign(_viewerWheelDelta)) _viewerWheelDelta = 0;
        _viewerWheelTime = timestamp;
        // 高精度滚轮累计到一格再切换；快速连滚最多每 160ms 切一项，不积压异步打开任务。
        if (timestamp - _viewerWheelSwitchTime < 160) { _viewerWheelDelta = 0; return; }
        _viewerWheelDelta = (int)Math.Clamp((long)_viewerWheelDelta + delta, -120, 120);
        if (Math.Abs(_viewerWheelDelta) < 120) return;
        int next = ViewerState.CurrentIndex + (_viewerWheelDelta > 0 ? -1 : 1);
        _viewerWheelDelta = 0;
        if (next < 0 || next >= ViewerState.Playlist.Count) return;
        _viewerWheelSwitchTime = timestamp;
        await ShowInViewerAsync(next);
    }
}
