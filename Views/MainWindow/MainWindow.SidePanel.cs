using Microsoft.UI.Xaml;

namespace Fallery;

public sealed partial class MainWindow
{
    private bool _applyingSidePanel;

    private void ResetViewerSidePanel()
    {
        _detailsOpen = false;
        _playlistOpen = false;
        ApplyViewerSidePanel();
    }

    private void InitializeViewerSidePanel()
    {
        // 使用系统默认阴影，不注册显式接收层，避免 Win10 初始化时触发 XAML 原生异常。
        ViewerSidePanel.Translation = new System.Numerics.Vector3(0, 0, 16);
    }

    private void ViewerStage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewerSidePanel.MaxWidth = Math.Max(0, e.NewSize.Width - ViewerSidePanel.Margin.Left - ViewerSidePanel.Margin.Right);
        UpdateViewerNextButtonPosition();
    }

    private void UpdateViewerNextButtonPosition()
    {
        double right = 12;
        if (ViewerSidePanel.Visibility == Visibility.Visible)
            right += ViewerSidePanel.Margin.Right + Math.Min(ViewerSidePanel.Width, ViewerSidePanel.MaxWidth);
        // 使用目标宽度，切换两种面板时不等待上一轮布局的 ActualWidth。
        NextButton.Margin = new Thickness(12, 12, right, 12);
    }

    private void ApplyViewerSidePanel()
    {
        if (_closed || _closing || _applyingSidePanel) return;
        _applyingSidePanel = true;
        try
        {
            // 右侧始终只显示一种内容。
            if (_detailsOpen) _playlistOpen = false;
            bool playlist = _playlistOpen && !IsFilmstrip;
            bool visible = _detailsOpen || playlist;
            ViewerSidePanel.Width = _detailsOpen ? 380 : 300;
            // 面板始终覆盖画面右侧，不参与画面测量，也不改变窗口位置或尺寸。
            ViewerSidePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            DetailsPanel.Visibility = _detailsOpen ? Visibility.Visible : Visibility.Collapsed;
            PlaylistPanel.Visibility = playlist ? Visibility.Visible : Visibility.Collapsed;
            DetailsToggle.IsChecked = _detailsOpen;
            PlaylistToggle.IsChecked = playlist;
            PlaylistToggle.Visibility = IsFilmstrip ? Visibility.Collapsed : Visibility.Visible;
            UpdateViewerNextButtonPosition();

            if (playlist) SyncPlaylistSelection();
        }
        finally { _applyingSidePanel = false; }
    }
}
