using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;

namespace Fallery;

public sealed partial class MainWindow
{
    private bool ApplyDefaultVideoWindowSize()
    {
        if (Root.XamlRoot is null || AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored }) return false;
        Root.UpdateLayout();
        // 恢复设置刚调用 Resize 时 XAML ActualSize 可能仍是旧值，系统客户区已是新尺寸。
        if (!GetClientRect(WinRT.Interop.WindowNative.GetWindowHandle(this), out var client)) return false;
        double scale = Root.XamlRoot.RasterizationScale;
        var controls = VideoControls.Visibility;
        var audio = VideoAudioPanel.Visibility;
        var zoom = ImageZoomPanel.Visibility;
        try
        {
            // 默认的 1280×720 指视频画面，窗口还需要容纳标题栏、两行控制栏和系统边框。
            VideoControls.Visibility = VideoAudioPanel.Visibility = Visibility.Visible;
            ImageZoomPanel.Visibility = Visibility.Collapsed;
            VideoControls.Measure(new Size(1280, double.PositiveInfinity));
            ViewerBar.Measure(new Size(1280, double.PositiveInfinity));
            double chromeWidth = Math.Max(0, AppWindow.Size.Width - (client.Right - client.Left))
                + PlaylistColumn.Width.Value * scale;
            double chromeHeight = Math.Max(0, AppWindow.Size.Height - (client.Bottom - client.Top))
                + (AppTitleBar.ActualHeight + VideoControls.DesiredSize.Height + ViewerBar.DesiredSize.Height) * scale;
            return ResizeWindowForVideo(16d / 9, 1280 * scale, chromeWidth, chromeHeight);
        }
        finally
        {
            VideoControls.Visibility = controls;
            VideoAudioPanel.Visibility = audio;
            ImageZoomPanel.Visibility = zoom;
        }
    }

    private async void FitVideoWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _closing || _videoPlayer is not { VideoWidth: > 0, VideoHeight: > 0 } player) return;
        bool restore = _presenterBeforeFullScreen is not null
            || AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        if (_presenterBeforeFullScreen is not null) SetViewerFullScreen(false);
        if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State != OverlappedPresenterState.Restored)
            presenter.Restore();
        if (restore)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            try { await WaitForVideoLayoutFrameAsync(cts.Token); }
            catch (OperationCanceledException) { return; }
        }
        if (!_closed && ReferenceEquals(_videoPlayer, player))
            FitWindowToVideoAspect((double)player.VideoWidth / player.VideoHeight);
    }

    private bool FitWindowToVideoAspect(double ratio)
    {
        if (_closed || _closing || !ViewerState.IsOpen || Root.XamlRoot is null
            || AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored }) return false;
        Root.UpdateLayout();
        double scale = Root.XamlRoot.RasterizationScale;
        double width = ViewerStage.ActualWidth * scale;
        double height = ViewerStage.ActualHeight * scale;
        if (width <= 0 || height <= 0) return false;
        bool resized = ResizeWindowForVideo(ratio, width, AppWindow.Size.Width - width, AppWindow.Size.Height - height);
        if (resized)
        {
            // 用户显式适配的新尺寸同时成为浏览尺寸，返回时不能被进入前的快照覆盖。
            if (!IsFilmstrip)
            {
                _browseWindowPlacement = null;
                RememberCurrentBrowseWindowPlacement();
            }
            _videoWindowDefaultsApplied = true;
            QueueSettingsSave();
        }
        return resized;
    }

    private bool ResizeWindowForVideo(double ratio, double desiredVideoWidth, double chromeWidth, double chromeHeight)
    {
        if (!double.IsFinite(ratio) || ratio <= 0) return false;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        double availableWidth = area.Width - chromeWidth;
        double availableHeight = area.Height - chromeHeight;
        if (availableWidth <= 0 || availableHeight <= 0) return false;
        double videoWidth = Math.Min(desiredVideoWidth, Math.Min(availableWidth, availableHeight * ratio));
        int width = Math.Min(area.Width, (int)Math.Round(videoWidth + chromeWidth));
        int height = Math.Min(area.Height, (int)Math.Round(videoWidth / ratio + chromeHeight));
        var position = AppWindow.Position;
        int x = Math.Clamp(position.X, area.X, Math.Max(area.X, area.X + area.Width - width));
        int y = Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - height));
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        _normalWidth = AppWindow.Size.Width;
        _normalHeight = AppWindow.Size.Height;
        _maximized = false;
        return true;
    }

    private void ViewerBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 窄窗口收起可在详细信息中查看的元数据，给播放操作留空间。
        ViewerMetadata.Visibility = e.NewSize.Width >= 850 ? Visibility.Visible : Visibility.Collapsed;
        VideoVolume.Visibility = e.NewSize.Width >= 600 ? Visibility.Visible : Visibility.Collapsed;
    }
}
