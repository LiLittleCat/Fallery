using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private int _viewerQuarterTurns;

    private void RotateMedia_Click(object sender, RoutedEventArgs e) => RotateViewerMedia();
    private void RotateImageLeft_Click(object sender, RoutedEventArgs e) => RotateViewerMediaBy(-1);

    private void RotateViewerMedia() => RotateViewerMediaBy(1);

    private void RotateViewerMediaBy(int direction)
    {
        if (_closed || _closing || !ViewerState.IsOpen) return;
        StopMotionPlayback();
        bool video = ViewerState.CurrentItem?.IsVideo == true;
        // 镜像与旋转不可交换；翻转后反向更新原图角度，画面仍按按钮所指方向旋转。
        int step = !video && ImageRotation.IsMirrored ? -direction : direction;
        int next = (_viewerQuarterTurns + step + 4) % 4;
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

    private void FlipImage_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _closing || !ViewerState.IsOpen || ViewerState.CurrentItem?.IsVideo != false
            || ViewerImage.Source is null)
        {
            // ToggleButton 会先切换自身状态；加载中不能留下与实际图像不符的选中态。
            UpdateRotationLabel();
            return;
        }
        ImageRotation.IsMirrored = !ImageRotation.IsMirrored;
        StopMotionPlayback();
        Root.UpdateLayout();
        UpdateRotationLabel();
    }

    private void ResetViewerRotation(bool keepImageButtonsEnabled = false)
    {
        _viewerQuarterTurns = 0;
        ImageRotation.QuarterTurns = 0;
        ImageRotation.IsMirrored = false;
        // 图片间切换只重置方向，不让三个按钮随解码过程反复变灰、变亮。
        if (!keepImageButtonsEnabled) SetImageTransformButtonsEnabled(false);
        UpdateRotationLabel();
    }

    private void SetImageTransformButtonsEnabled(bool enabled)
    {
        RotateMediaButton.IsEnabled = enabled;
        RotateImageLeftButton.IsEnabled = enabled;
        FlipImageToggle.IsEnabled = enabled;
    }

    private void UpdateRotationLabel()
    {
        string label = "向右旋转 90°";
        ToolTipService.SetToolTip(RotateMediaButton, label);
        AutomationProperties.SetName(RotateMediaButton, label);
        FlipImageToggle.IsChecked = ImageRotation.IsMirrored;
        string flipLabel = ImageRotation.IsMirrored ? "取消水平翻转" : "水平翻转";
        ToolTipService.SetToolTip(FlipImageToggle, flipLabel);
        AutomationProperties.SetName(FlipImageToggle, flipLabel);
    }
}
