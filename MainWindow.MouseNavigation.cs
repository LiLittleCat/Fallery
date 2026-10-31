using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Fallery;

public sealed partial class MainWindow
{
    private async void Root_MouseNavigationPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        var kind = e.GetCurrentPoint(Root).Properties.PointerUpdateKind;
        if (kind is not (PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton2Pressed)) return;
        // 只处理本次按下的侧键，按住侧键再点其他按钮不会重复跳转。
        e.Handled = true;
        await HandleMouseNavigationAsync(kind == PointerUpdateKind.XButton2Pressed);
    }

    private async Task HandleMouseNavigationAsync(bool forward)
    {
        if (_closed || _closing || _settingsDialog is not null || _deleting || _pickingFolder
            || AddressEditor.Visibility == Visibility.Visible || _addressFlyout is not null || _itemMenu is not null
            || (Root.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
                .Any(popup => popup.Child is not ToolTip))) return;

        // 横排预览仍属于目录浏览；进入覆盖式或全屏查看器后只允许视频跳转播放位置。
        if (!ViewerState.IsOpen || (IsFilmstrip && _presenterBeforeFullScreen is null))
        {
            if (forward) await GoForwardAsync();
            else await GoBackAsync();
        }
        else if (ViewerState.CurrentItem?.IsVideo == true && ViewerVideo.Opacity >= 1 && _videoPlayer is not null)
        {
            SeekVideo(TimeSpan.FromSeconds(forward ? 5 : -5));
        }
    }
}
