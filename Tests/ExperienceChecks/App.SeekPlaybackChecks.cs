using System.Diagnostics;
using System.Reflection;
using Fallery.Models;
using Fallery.Services.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunSeekPlaybackChecksAsync(MainWindow window, string path, string report)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var video = (MediaPlayerElement)((FrameworkElement)window.Content).FindName("ViewerVideo");
        async Task Until(Func<bool> ready)
        {
            for (int i = 0; i < 150 && !ready(); i++) await Task.Delay(50);
            Check(ready(), "真实静音播放器没有及时就绪");
        }
        var playlist = window.ViewerState.Playlist;
        // 不带文件版本，避免测量过程写入用户的续播记录。
        window.ViewerState.SetPlaylist([new MediaItem(path, MediaKind.Video)]);
        try
        {
            await (Task)Call("ShowInViewerAsync", 0, true)!;
            await Until(() => video.MediaPlayer is { PlaybackSession.NaturalDuration.TotalSeconds: > 0 } && video.Opacity == 1);
            var player = (IPlayer)typeof(MainWindow).GetField("_videoPlayer", flags)!.GetValue(window)!;
            await Until(() => typeof(WindowsMediaPlayer).GetField("_keyframes", flags)!.GetValue(player) is not null);
            Check(!video.MediaPlayer.RealTimePlayback && player.IsMuted && player.Volume == 0,
                "正式视频使用默认文件播放调度，测试保持静音及零音量");
            var session = video.MediaPlayer.PlaybackSession;
            TaskCompletionSource? completion = null;
            void Completed(Windows.Media.Playback.MediaPlaybackSession sender, object args) => completion?.TrySetResult();
            session.SeekCompleted += Completed;
            try
            {
                foreach (bool playing in new[] { false, true })
                {
                    player.Pause();
                    await Until(() => !player.IsPlaying);
                    completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    player.Position = player.Duration * 0.3;
                    await Until(() => !(bool)typeof(WindowsMediaPlayer).GetField("_seeking", flags)!.GetValue(player)!);
                    if (playing) { player.Play(); await Until(() => player.IsPlaying); }
                    foreach (int direction in new[] { 1, -1 })
                    {
                        var delta = TimeSpan.FromSeconds(Math.Min(5, player.Duration.TotalSeconds / 10) * direction);
                        double target = (player.Position + delta).TotalSeconds;
                        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        var elapsed = Stopwatch.StartNew();
                        Call("SeekVideo", delta);
                        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
                        await Until(() => player.IsPlaying == playing
                            && !(bool)typeof(WindowsMediaPlayer).GetField("_seeking", flags)!.GetValue(player)!);
                        File.AppendAllText(report, $"\nSEEK playing={playing}, delta={delta.TotalSeconds:F2}s, completed={elapsed.Elapsed.TotalMilliseconds:F1}ms");
                        Check(Math.Abs(player.Position.TotalSeconds - target) < 3.5 && player.IsPlaying == playing,
                            "生产快速跳转必须到达目标附近关键帧并保持播放或暂停状态");
                        if (playing)
                        {
                            double afterSeek = player.Position.TotalSeconds;
                            await Until(() => player.Position.TotalSeconds >= afterSeek + 0.15);
                            File.AppendAllText(report, $"\nSEEK clock advanced150ms after {elapsed.Elapsed.TotalMilliseconds:F1}ms");
                        }
                    }
                }
                player.Play();
                await Until(() => player.IsPlaying);
                player.Position = player.Duration * 0.2;
                player.Position = player.Duration * 0.7;
                player.Position = player.Duration * 0.4;
                player.Pause();
                await Until(() => !(bool)typeof(WindowsMediaPlayer).GetField("_seeking", flags)!.GetValue(player)!);
                Check(!player.IsPlaying && Math.Abs(session.Position.TotalSeconds - player.Duration.TotalSeconds * 0.4) < 0.5,
                    "连续定位应以最后目标为准，中途暂停不能被定位完成回调覆盖");
                player.Position = player.Duration * 0.6;
                player.Play();
                await Until(() => !(bool)typeof(WindowsMediaPlayer).GetField("_seeking", flags)!.GetValue(player)!
                    && session.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing);
                Check(Math.Abs(session.Position.TotalSeconds - player.Duration.TotalSeconds * 0.6) < 0.5,
                    "定位中请求播放应在最终目标完成后恢复");
                player.Pause();
                await Until(() => session.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Paused);
                player.Position = player.Duration * 0.8;
                await Until(() => !(bool)typeof(WindowsMediaPlayer).GetField("_seeking", flags)!.GetValue(player)!);
                var requested = player.Duration * 0.5;
                var frames = typeof(WindowsMediaPlayer).GetField("_keyframes", flags)!.GetValue(player)!;
                var snapped = (TimeSpan)frames.GetType().GetMethod("Nearest")!.Invoke(frames, [requested, player.Position, player.Duration])!;
                var slider = (Slider)((FrameworkElement)window.Content).FindName("VideoSeek");
                slider.Value = requested.TotalSeconds;
                await Until(() => !(bool)typeof(WindowsMediaPlayer).GetField("_seeking", flags)!.GetValue(player)!);
                Check(Math.Abs((session.Position - snapped).TotalSeconds) < 0.1 && !player.IsPlaying,
                    "进度条必须使用真实关键帧索引并保留暂停状态");
                File.AppendAllText(report, $"\nKEYFRAME requested={requested.TotalSeconds:F3}, actual={session.Position.TotalSeconds:F3}");
            }
            finally { session.SeekCompleted -= Completed; }
        }
        finally
        {
            Call("CloseViewer");
            window.ViewerState.SetPlaylist(playlist);
        }
        var factory = typeof(MainWindow).GetMethod("CreatePlayer", flags, [typeof(MediaItem), typeof(CancellationToken)])!;
        using (var audio = (IPlayer)factory.Invoke(window, [new MediaItem(Path.ChangeExtension(path, ".wav"), MediaKind.Audio), CancellationToken.None])!)
            Check(!video.MediaPlayer.RealTimePlayback, "音频播放器保留默认缓冲模式");
        Call("StopVideo", false);
        var previewView = new MediaPlayerElement();
        using (var preview = new WindowsMediaPlayer(previewView, enableSystemTransportControls: false) { IsMuted = true, Volume = 0 })
            Check(!previewView.MediaPlayer.RealTimePlayback && !previewView.MediaPlayer.CommandManager.IsEnabled,
                "悬停预览不启用低延迟或系统媒体控制");
        return checks;
    }
}
