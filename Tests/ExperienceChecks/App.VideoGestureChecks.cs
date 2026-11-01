using System.Reflection;
using Fallery.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunVideoGestureChecksAsync(MainWindow window)
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks++;
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "experience-checks.txt"), $"\nGESTURE {checks}: {message}");
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        object? Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        var root = (FrameworkElement)window.Content;
        var video = (MediaPlayerElement)root.FindName("ViewerVideo");
        var player = new MouseSeekPlayer { IsMuted = true, Volume = 0 };
        var playlist = window.ViewerState.Playlist;
        window.ViewerState.SetPlaylist([new MediaItem(Path.Combine(AppContext.BaseDirectory, "gesture-check.mp4"), MediaKind.Video)]);
        window.ViewerState.BeginOpen(0);
        typeof(MainWindow).GetField("_videoPlayer", flags)!.SetValue(window, player);
        video.Opacity = 1;
        root.UpdateLayout();
        void Press(PointerUpdateKind kind) => Call("RecordVideoMousePress", kind);
        void Tap(bool twice = false, PointerDeviceType device = PointerDeviceType.Mouse) => Call("HandleVideoTap", device, twice);
        bool FullScreen() => Get("_presenterBeforeFullScreen") is not null;
        // 隐藏宿主不能进入系统全屏；沿用现有检查方式建立全屏状态，验证双击退出路径。
        void SetFullScreenState() => typeof(MainWindow).GetField("_presenterBeforeFullScreen", flags)!
            .SetValue(window, window.AppWindow.Presenter);
        try
        {
            foreach (bool playing in new[] { false, true })
            {
                if (playing) player.Play(); else player.Pause();
                for (int i = 0; i < 8; i++)
                {
                    bool forward = i % 2 == 0;
                    Press(forward ? PointerUpdateKind.XButton2Pressed : PointerUpdateKind.XButton1Pressed);
                    await (Task)Call("HandleMouseNavigationAsync", forward)!;
                    Tap(); Tap(twice: true);
                    Check(player.Position == TimeSpan.FromSeconds(forward ? 25 : 20)
                        && player.IsPlaying == playing && !FullScreen(),
                        "侧键快进快退及后续单击/双击手势不得改变播放、暂停或全屏状态");
                }
            }
            foreach (var kind in new[] { PointerUpdateKind.RightButtonPressed, PointerUpdateKind.MiddleButtonPressed })
            {
                Press(kind); Tap(); Tap(twice: true);
                Check(player.IsPlaying && !FullScreen(), "右键和中键不得切换播放或全屏");
            }
            foreach (bool playing in new[] { false, true })
            {
                if (playing) player.Play(); else player.Pause();
                Press(PointerUpdateKind.LeftButtonPressed); Tap();
                Check(player.IsPlaying != playing, "左键单击应切换播放/暂停");
                SetFullScreenState();
                Press(PointerUpdateKind.LeftButtonPressed); Tap(twice: true);
                Check(player.IsPlaying == playing && !FullScreen(), "左键双击应退出全屏并恢复第一下前的播放状态");
            }
            Press(PointerUpdateKind.XButton1Pressed); Tap();
            SetFullScreenState();
            Press(PointerUpdateKind.LeftButtonPressed); Tap(twice: true);
            Check(player.IsPlaying && !FullScreen(), "未处理过第一下单击时，双击不能凭空反转播放状态");
            Tap(device: PointerDeviceType.Touch);
            Check(!player.IsPlaying, "触摸单击保留暂停功能");
            SetFullScreenState();
            Tap(twice: true, device: PointerDeviceType.Touch);
            Check(player.IsPlaying && !FullScreen(), "触摸双击保留退出全屏及播放状态");
        }
        finally
        {
            Call("CloseViewer");
            window.ViewerState.SetPlaylist(playlist);
        }
        return checks;
    }
}
