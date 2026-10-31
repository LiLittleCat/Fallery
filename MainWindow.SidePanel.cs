using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Fallery;

public sealed partial class MainWindow
{
    private const double ViewerSidePanelWidth = 300;
    private bool _dockViewerPanel = true;
    private bool _applyingSidePanel;

    private void ApplyViewerSidePanel(bool resizeWindow = false)
    {
        if (_closed || _closing || _applyingSidePanel) return;
        _applyingSidePanel = true;
        try
        {
            // 兼容旧设置中两个面板同时打开的状态，右侧始终只显示一种内容。
            if (_detailsOpen) _playlistOpen = false;
            bool playlist = _playlistOpen && !IsFilmstrip;
            bool visible = _detailsOpen || playlist;
            bool normal = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored };
            double scale = Root.XamlRoot?.RasterizationScale ?? 1;
            double oldWidth = PlaylistColumn.Width.Value;
            var size = AppWindow.Size;
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            bool resize = resizeWindow && normal && !IsFilmstrip && ViewerState.IsOpen && Root.XamlRoot is not null;

            // 无法向右扩展时（含最大化、全屏），面板在画面上方展开，不挤压视频。
            if (resize && visible && ViewerSidePanel.Visibility != Visibility.Visible)
                _dockViewerPanel = size.Width + (ViewerSidePanelWidth - oldWidth) * scale <= area.Width;
            double newWidth = visible && normal && !IsFilmstrip && _dockViewerPanel ? ViewerSidePanelWidth : 0;
            PlaylistColumn.Width = new GridLength(newWidth);
            // 停靠时填满独立侧栏，浮层只覆盖画面，底部开关始终可用。
            Grid.SetColumn(ViewerSidePanel, newWidth > 0 ? 1 : 0);
            Grid.SetColumnSpan(ViewerSidePanel, 1);
            Grid.SetRowSpan(ViewerSidePanel, newWidth > 0 ? 3 : 1);
            ViewerSidePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            DetailsPanel.Visibility = _detailsOpen ? Visibility.Visible : Visibility.Collapsed;
            PlaylistPanel.Visibility = playlist ? Visibility.Visible : Visibility.Collapsed;
            DetailsToggle.IsChecked = _detailsOpen;
            PlaylistToggle.IsChecked = playlist;
            PlaylistToggle.Visibility = IsFilmstrip ? Visibility.Collapsed : Visibility.Visible;

            if (resize && newWidth != oldWidth)
            {
                // 只改变右边缘，视频区域及控制栏高度保持不变；两种面板间切换不改变窗口。
                int width = Math.Max(1, size.Width + (int)Math.Round((newWidth - oldWidth) * scale));
                int x = Math.Clamp(AppWindow.Position.X, area.X, Math.Max(area.X, area.X + area.Width - width));
                AppWindow.MoveAndResize(new RectInt32(x, AppWindow.Position.Y, width, size.Height));
            }
            if (playlist) SyncPlaylistSelection();
        }
        finally { _applyingSidePanel = false; }
    }
}
