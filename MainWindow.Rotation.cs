using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private int _viewerQuarterTurns;

    private void RotateMedia_Click(object sender, RoutedEventArgs e) => RotateViewerMedia();

    private void RotateViewerMedia()
    {
        if (_closed || _closing || !ViewerState.IsOpen) return;
        int next = (_viewerQuarterTurns + 1) % 4;
        if (ViewerState.CurrentItem?.IsVideo == true)
        {
            if (_videoPlayer is not { VideoWidth: > 0, VideoHeight: > 0 } player) return;
            try { player.Rotation = next * 90; }
            catch (Exception ex)
            {
                GalleryState.Status = $"无法旋转视频：{ex.Message}";
                return;
            }
            _viewerQuarterTurns = next;
            VideoSurface.AspectRatio = (double)player.VideoWidth / player.VideoHeight;
        }
        else
        {
            if (ViewerImage.Source is null) return;
            _viewerQuarterTurns = next;
            ImageRotation.QuarterTurns = next;
            Root.UpdateLayout();
            FitImageToViewport();
            UpdateZoomIndicators();
        }
        UpdateRotationLabel();
    }

    private void ResetViewerRotation()
    {
        _viewerQuarterTurns = 0;
        ImageRotation.QuarterTurns = 0;
        RotateMediaButton.IsEnabled = false;
        UpdateRotationLabel();
    }

    private void UpdateRotationLabel()
    {
        string label = $"顺时针旋转 90°（当前 {_viewerQuarterTurns * 90}°）";
        ToolTipService.SetToolTip(RotateMediaButton, label);
        AutomationProperties.SetName(RotateMediaButton, label);
    }
}
