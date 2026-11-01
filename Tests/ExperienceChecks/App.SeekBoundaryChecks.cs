using System.Reflection;
using Fallery.Models;
using Fallery.Services.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Playback;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunSeekBoundaryChecksAsync(MainWindow window, string path, string report)
    {
        int checks = 0;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var playerField = typeof(MainWindow).GetField("_videoPlayer", flags)!;
        IPlayer Player() => (IPlayer)playerField.GetValue(window)!;
        var root = (FrameworkElement)window.Content;
        var video = (MediaPlayerElement)root.FindName("ViewerVideo");
        var slider = (Slider)root.FindName("VideoSeek");
        async Task Until(Func<bool> ready, string message)
        {
            for (int i = 0; i < 120 && !ready(); i++) await Task.Delay(50);
            if (!ready()) throw new InvalidOperationException(message);
            checks++;
            File.AppendAllText(report, $"\nBOUNDARY {checks}: {message}");
        }
        bool Seeking(IPlayer player) => (bool)typeof(WindowsMediaPlayer).GetField("_seeking", flags)!.GetValue(player)!;
        async Task Open(int index)
        {
            await (Task)Call("ShowInViewerAsync", index, true)!;
            await Until(() => video.Opacity == 1 && video.MediaPlayer?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing,
                "样本应自动开始播放");
            if (!Player().IsMuted || Player().Volume != 0) throw new InvalidOperationException("测试必须保持静音");
        }
        var oldPlaylist = window.ViewerState.Playlist;
        window.ViewerState.SetPlaylist([new MediaItem(path, MediaKind.Video), new MediaItem(path, MediaKind.Video)]);
        try
        {
            await Open(0);
            var player = Player();
            var session = video.MediaPlayer.PlaybackSession;
            int chained = 0;
            // 在包装器刚发出 Play、原生状态尚未更新的同一完成回调里再次跳到开头。
            void Chain(MediaPlaybackSession sender, object args)
            {
                if (Interlocked.Exchange(ref chained, 1) == 0) player.Seek(TimeSpan.Zero);
            }
            session.SeekCompleted += Chain;
            try
            {
                player.Position = player.Duration * 0.4;
                await Until(() => chained == 1 && !Seeking(player) && session.PlaybackState == MediaPlaybackState.Playing
                    && session.Position.TotalSeconds > 0.2 && session.Position.TotalSeconds < 1.5,
                    "恢复播放回调中再次回到开头，仍应继续播放");
            }
            finally { session.SeekCompleted -= Chain; }

            // 模拟定位内部暂停后、原生 Play 尚未生效的状态，不发送用户 Pause。
            video.MediaPlayer.Pause();
            await Until(() => session.PlaybackState == MediaPlaybackState.Paused, "构造系统时钟临时暂停状态");
            player.Seek(TimeSpan.Zero);
            await Until(() => !Seeking(player) && session.PlaybackState == MediaPlaybackState.Playing
                && session.Position.TotalSeconds > 0.2 && session.Position.TotalSeconds < 1.5,
                "系统临时暂停不能覆盖用户继续播放的意图");

            foreach (bool sideKey in new[] { false, true })
            {
                player.Position = player.Duration * 0.4;
                await Until(() => !Seeking(player), "边界检查前中间定位完成");
                Call("UpdateVideoProgress");
                if (sideKey) Call("SeekVideo", -player.Duration * 2);
                else slider.Value = 0;
                await Until(() => !Seeking(player) && session.PlaybackState == MediaPlaybackState.Playing
                    && session.Position.TotalSeconds > 0.2 && session.Position.TotalSeconds < 1.5,
                    "点击/快退到开头后继续播放");
            }
            // Pause 后紧接定位，不能被尚未到达的原生 Paused 通知覆盖。
            player.Pause();
            player.Position = player.Duration * 0.4;
            await Until(() => !Seeking(player) && session.PlaybackState == MediaPlaybackState.Paused && !player.IsPlaying,
                "用户主动暂停后立即定位仍保持暂停");
            player.Seek(TimeSpan.Zero);
            await Until(() => !Seeking(player) && !player.IsPlaying, "暂停状态回到开头不会擅自播放");
            player.Seek(player.Duration);
            await Until(() => !Seeking(player) && !player.IsPlaying && window.ViewerState.CurrentIndex == 0,
                "暂停状态定位到结尾保持当前项，不擅自连播");

            await Open(0);
            Call("StopVideo", false);
            // 明确模拟已复现的系统异常：时钟越过总长，始终不发送 Ended。
            var noEndEvent = new MouseSeekPlayer { IsMuted = true, Volume = 0, Position = TimeSpan.FromSeconds(65) };
            noEndEvent.Play();
            playerField.SetValue(window, noEndEvent);
            Call("UpdateVideoProgress");
            await Until(() => window.ViewerState.CurrentIndex == 1 && !ReferenceEquals(Player(), noEndEvent)
                && video.Opacity == 1 && video.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing,
                "系统未发送 Ended、时钟越界时也应自动播放下一项");
            var nextPlayer = Player();
            await (Task)Call("CompletePlaybackAsync", noEndEvent)!;
            await Until(() => ReferenceEquals(Player(), nextPlayer) && window.ViewerState.CurrentIndex == 1,
                "迟到的旧播放器结束事件不能重复推进");

            await Open(0);
            player = Player();
            player.Position = player.Duration - TimeSpan.FromMilliseconds(400);
            await Until(() => window.ViewerState.CurrentIndex == 1 && !ReferenceEquals(Player(), player)
                && video.Opacity == 1 && video.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing,
                "自然播放到结尾仍应自动连播");

            foreach (bool sideKey in new[] { false, true })
            {
                await Open(0);
                player = Player();
                Call("UpdateVideoProgress");
                if (sideKey) Call("SeekVideo", player.Duration * 2);
                else slider.Value = slider.Maximum;
                await Until(() => window.ViewerState.CurrentIndex == 1 && !ReferenceEquals(Player(), player)
                    && video.Opacity == 1 && video.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing,
                    "点击/快进到结尾应自动切换并播放下一项");
            }
            // 最后一项结束后不应一直处于 seeking；点击播放应从头重新开始。
            player = Player();
            player.Seek(player.Duration + TimeSpan.FromSeconds(5));
            await Until(() => !Seeking(player) && !player.IsPlaying, "最后一项到结尾后应完成并停止");
            Call("ToggleVideoPlayback");
            await Until(() => !Seeking(player) && video.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing
                && player.Position.TotalSeconds > 0.2 && player.Position.TotalSeconds < 1.5,
                "最后一项结束后可以从头重新播放");
        }
        finally
        {
            Call("CloseViewer");
            window.ViewerState.SetPlaylist(oldPlaylist);
        }
        return checks;
    }
}
