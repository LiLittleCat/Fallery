using System.Reflection;
using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Fallery.Services.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunAudioChecksAsync(MainWindow window, string workspace)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        IPlayer? Player() => (IPlayer?)typeof(MainWindow).GetField("_videoPlayer", flags)!.GetValue(window);
        async Task Until(Func<bool> condition)
        {
            for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(100);
            Check(condition(), "音频播放器未在期限内就绪");
        }
        var root = (FrameworkElement)window.Content;
        Call("CloseViewer");
        typeof(MainWindow).GetField("_muted", flags)!.SetValue(window, true);
        typeof(MainWindow).GetField("_volume", flags)!.SetValue(window, 0d);
        string folder = Path.Combine(workspace, "work", "experience-checks", "audio");
        string first = Path.Combine(folder, "01.wav"), second = Path.Combine(folder, "02.wav");
        AudioFixture.Write(first); AudioFixture.Write(second);
        var items = new[] { new MediaItem(first, MediaKind.Audio, FileVersion.Read(first)), new MediaItem(second, MediaKind.Audio, FileVersion.Read(second)) };
        window.ViewerState.SetPlaylist(items);
        await (Task)Call("ShowInViewerAsync", 0, true)!;
        await Until(() => Player() is { Duration.TotalSeconds: > 0, IsPlaying: true });
        var player = Player()!;
        Check(player is { IsMuted: true, Volume: 0, VideoWidth: 0, VideoHeight: 0 }, "音频必须使用真实静音播放器且没有视频画面尺寸");
        Check(((Grid)root.FindName("AudioPresentation")).Visibility == Visibility.Visible
            && ((Grid)root.FindName("VideoControls")).Visibility == Visibility.Visible
            && ((StackPanel)root.FindName("ImageZoomPanel")).Visibility == Visibility.Collapsed,
            "音频显示专用封面区及播放控制，不显示图片缩放");
        Check(((Button)root.FindName("RotateMediaButton")).Visibility == Visibility.Collapsed
            && ((Button)root.FindName("FitVideoWindowButton")).Visibility == Visibility.Collapsed, "音频不显示旋转与视频适配");
        Call("VideoMoreMenu_Opening", new object(), new object());
        Check(((MenuFlyoutItem)root.FindName("VideoStepBackItem")).Visibility == Visibility.Collapsed
            && ((MenuFlyoutItem)root.FindName("VideoScreenshotItem")).Visibility == Visibility.Collapsed, "音频更多菜单隐藏逐帧和截图");
        Call("ToggleVideoPlayback");
        await Until(() => !player.IsPlaying);
        Call("SeekVideo", TimeSpan.FromSeconds(3));
        Check(player.Position.TotalSeconds >= 2, "音频支持进度跳转");
        Call("ChangeRate", 1);
        Check(player.Rate > 1, "音频支持倍速");
        Call("ChangeRate", 0);
        await items[0].EnsureThumbnailAsync(128);
        var tile = new MediaTile(); tile.Bind(items[0]);
        Check(((FontIcon)tile.FindName("AudioPlaceholder")).Visibility == Visibility.Visible
            && ((TextBlock)tile.FindName("FailureText")).Visibility == Visibility.Collapsed,
            "无封面音频显示音符，不显示预览失败");
        tile.Bind(null);
        Check(((FontIcon)tile.FindName("AudioPlaceholder")).Visibility == Visibility.Collapsed, "回收卡片清空音频状态");
        player.Position = player.Duration - TimeSpan.FromMilliseconds(250);
        Call("ToggleVideoPlayback");
        await Until(() => window.ViewerState.CurrentIndex == 1 && Player() is { IsPlaying: true });
        Check(Player() is { IsMuted: true, Volume: 0 }, "自动连播下一音频保持静音设置");
        Call("CloseViewer");
        Check(Player() is null && ((Grid)root.FindName("AudioPresentation")).Visibility == Visibility.Collapsed
            && ((Image)root.FindName("AudioArtwork")).Source is null, "关闭释放音频播放器及封面");
        return checks;
    }
}
