using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Fallery.Services.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fallery;

public sealed partial class MainWindow
{
    private MotionPhotoClip? _motionClip;
    private CancellationToken _motionPhotoToken;
    private CancellationTokenSource? _motionPlaybackCts;
    private IPlayer? _motionPlayer;
    private string? _motionTemporaryFile;
    private bool? _motionMuted;
    private double? _motionVolume;

    private static IPlayer CreatePlayer(MediaPlayerElement surface, bool enableSystemTransportControls)
        => new WindowsMediaPlayer(surface, enableSystemTransportControls);

    private async Task DiscoverMotionPhotoAsync(MediaItem item, CancellationToken ct)
    {
        try
        {
            var clip = await MotionPhoto.FindAsync(item.Path, ct);
            if (_closed || ct.IsCancellationRequested || !ReferenceEquals(ViewerState.CurrentItem, item)) return;
            _motionClip = clip;
            _motionPhotoToken = ct;
            MotionPhotoControls.Visibility = clip is null ? Visibility.Collapsed : Visibility.Visible;
            UpdateMotionSound();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* 动态元数据读取失败不影响静态照片。 */ }
    }

    private async void MotionPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (_motionPlaybackCts is not null) StopMotionPlayback();
        else await PlayMotionPhotoAsync();
    }

    private async Task PlayMotionPhotoAsync()
    {
        if (_motionClip is not { } clip || _closed || _closing || _motionPlaybackCts is not null) return;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_motionPhotoToken, _lifetimeCts.Token);
        _motionPlaybackCts = cts;
        LiveText.Set(MotionPhotoText, "Text", localized => localized.Text = AppText.Get("Loading"));
        ViewerState.Message = "";
        string? temporary = null;
        try
        {
            temporary = await MotionPhoto.ExtractAsync(clip, AppIdentity.MotionDirectory, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            _motionTemporaryFile = temporary;
            // 使用同一个系统播放内核，但不接入普通视频的连播、进度保存和媒体快捷键。
            IPlayer player = CreatePlayer(MotionVideo, enableSystemTransportControls: false);
            _motionPlayer = player;
            player.Volume = _motionVolume ?? _volume;
            player.IsMuted = _motionMuted ?? _muted;
            player.Rotation = _viewerQuarterTurns * 90;
            MotionVideo.RenderTransform = new ScaleTransform { ScaleX = ImageRotation.IsMirrored ? -1 : 1 };
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.Opened += (_, _) => DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (cts.IsCancellationRequested || !ReferenceEquals(_motionPlayer, player)) return;
                    if (player.VideoHeight > 0) MotionSurface.AspectRatio = (double)player.VideoWidth / player.VideoHeight;
                    Root.UpdateLayout();
                    await WaitForVideoLayoutFrameAsync(cts.Token);
                    if (!ReferenceEquals(_motionPlayer, player)) return;
                    ImageScroller.Opacity = 0;
                    MotionVideo.Opacity = 1;
                    LiveText.Set(MotionPhotoText, "Text", localized => localized.Text = AppText.Get("StopMotion"));
                    LiveText.Set(MotionPhotoButton, "AutomationProperties.Name", localized => AutomationProperties.SetName(localized, AppText.Get("StopMotionAndReturnToPhoto")));
                    player.Play();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { finished.TrySetException(ex); }
            });
            player.Ended += (_, _) => finished.TrySetResult();
            player.Failed += (_, error) => finished.TrySetException(new IOException(error));
            MotionSurface.Visibility = Visibility.Visible;
            MotionVideo.Opacity = 0;
            UpdateMotionSound();
            await player.OpenAsync(temporary, cts.Token);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(60), cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && !cts.IsCancellationRequested && ReferenceEquals(_motionPlaybackCts, cts))
                ViewerState.Message = AppText.Format("CouldNotPlayTheMotionPhotoWindowsMayBe", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_motionPlaybackCts, cts)) StopMotionPlayback();
            if (temporary is not null) await MotionPhoto.DeleteTemporaryAsync(temporary);
        }
    }

    private void StopMotionPlayback()
    {
        var cts = _motionPlaybackCts;
        _motionPlaybackCts = null;
        cts?.Cancel();
        var player = _motionPlayer;
        _motionPlayer = null;
        player?.Dispose();
        MotionSurface.Visibility = Visibility.Collapsed;
        MotionVideo.Opacity = 0;
        ImageScroller.Opacity = 1;
        LiveText.Set(MotionPhotoText, "Text", localized => localized.Text = AppText.Get("Motion"));
        LiveText.Set(MotionPhotoButton, "AutomationProperties.Name", localized => AutomationProperties.SetName(localized, AppText.Get("PlayMotionPhoto")));
        if (_motionTemporaryFile is { } path) _ = MotionPhoto.DeleteTemporaryAsync(path);
        _motionTemporaryFile = null;
    }

    private void ResetMotionPhoto()
    {
        StopMotionPlayback();
        _motionClip = null;
        MotionPhotoControls.Visibility = Visibility.Collapsed;
    }

    private void MotionSound_Click(object sender, RoutedEventArgs e)
    {
        bool silent = (_motionMuted ?? _muted) || (_motionVolume ?? _volume) == 0;
        _motionMuted = !silent;
        if (silent && (_motionVolume ?? _volume) == 0) _motionVolume = 100;
        if (_motionPlayer is { } player)
        {
            player.IsMuted = _motionMuted.Value;
            player.Volume = _motionVolume ?? _volume;
        }
        UpdateMotionSound();
    }

    private void UpdateMotionSound()
    {
        bool silent = (_motionMuted ?? _muted) || (_motionVolume ?? _volume) == 0;
        MotionSoundIcon.Glyph = Controls.FluentIcons.Glyph(silent ? "IconMute" : "IconVolume");
        string label = silent ? AppText.Get("UnmuteMotionPhoto") : AppText.Get("MuteMotionPhoto");
        LiveText.SetOnce(MotionSoundButton, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, label));
        LiveText.SetOnce(MotionSoundButton, "AutomationProperties.Name", localized => AutomationProperties.SetName(localized, label));
    }
}
