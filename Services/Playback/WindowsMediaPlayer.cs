using Fallery.Services;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Storage;

namespace Fallery.Services.Playback;

/// <summary>基于 Windows Media Foundation 的实现；格式支持取决于系统已安装的解码器。</summary>
public sealed class WindowsMediaPlayer : IPlayer
{
    private readonly MediaPlayerElement _view;
    private readonly MediaPlayer _player = new() { AutoPlay = false };
    private MediaPlaybackItem? _item;
    private MediaSource? _source;
    private readonly object _seekGate = new();
    private bool _seeking;
    private bool _playRequested;
    private bool _disposed;
    private TimeSpan _seekTarget;
    private TimeSpan _inflightTarget;
    private int _stepAfterSeek;
    private VideoKeyframes? _keyframes;
    private CancellationTokenSource? _keyframesCancellation;
    private string? _path;
    private long _traceUntil;
    private long _traceLastPosition;
    private readonly string _traceId = Guid.NewGuid().ToString("N")[..6];

    private void Trace(string action)
    {
        if (!PlaybackTrace.Enabled) return;
        try { PlaybackTrace.Write($"player={_traceId} {action} state={_player.PlaybackSession.PlaybackState} position={_player.PlaybackSession.Position.TotalSeconds:F3} realtime={_player.RealTimePlayback}"); }
        catch (Exception) { /* 播放器关闭时的诊断回调不能影响生命周期。 */ }
    }

    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event EventHandler? StateChanged;
    public event EventHandler<string>? Failed;
    public event EventHandler? TracksChanged;

    public WindowsMediaPlayer(MediaPlayerElement view, bool enableSystemTransportControls = true)
    {
        _view = view;
        // 保留文件播放默认调度；RealTimePlayback 的快速回调不代表视频帧已经恢复呈现。
        if (!enableSystemTransportControls) _player.CommandManager.IsEnabled = false;
        _player.CommandManager.PlayReceived += (_, args) => { args.Handled = true; Play(); };
        _player.CommandManager.PauseReceived += (_, args) => { args.Handled = true; Pause(); };
        _player.MediaOpened += (_, _) =>
        {
            lock (_seekGate)
            {
                if (_disposed) return;
                if (enableSystemTransportControls && VideoWidth > 0 && _path is { } path && _keyframesCancellation is { } cancellation)
                    _ = LoadKeyframesAsync(path, Duration, cancellation.Token);
            }
            Opened?.Invoke(this, EventArgs.Empty);
        };
        _player.MediaEnded += (_, _) =>
        {
            ResetPlaybackIntent();
            Trace("ended");
            Ended?.Invoke(this, EventArgs.Empty);
        };
        _player.MediaFailed += (_, args) =>
        {
            ResetPlaybackIntent();
            Failed?.Invoke(this, args.ErrorMessage);
        };
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _player.PlaybackSession.SeekCompleted += OnSeekCompleted;
        if (PlaybackTrace.Enabled)
        {
            _player.PlaybackSession.PlaybackStateChanged += (_, _) => Trace("state");
            _player.PlaybackSession.SeekCompleted += (_, _) => Trace("seek-completed");
            _player.PlaybackSession.BufferingStarted += (_, _) => Trace("buffer-start");
            _player.PlaybackSession.BufferingEnded += (_, _) => Trace("buffer-end");
        }
        _view.SetMediaPlayer(_player);
    }

    private async Task LoadKeyframesAsync(string path, TimeSpan duration, CancellationToken ct)
    {
        var frames = await VideoKeyframes.LoadAsync(path, duration, ct).ConfigureAwait(false);
        lock (_seekGate)
        {
            if (_disposed || ct.IsCancellationRequested) return;
            _keyframes = frames;
            Trace($"keyframes-ready available={frames is not null}");
        }
    }

    public void Seek(TimeSpan target)
    {
        lock (_seekGate)
        {
            if (_disposed) return;
            var snapped = _keyframes?.Nearest(target, Position, Duration) ?? target;
            Trace($"seek-fast requested={target.TotalSeconds:F3} target={snapped.TotalSeconds:F3}");
            Position = snapped;
        }
    }

    public TimeSpan Position
    {
        get
        {
            if (PlaybackTrace.Enabled && Environment.TickCount64 < _traceUntil && Environment.TickCount64 - _traceLastPosition >= 200)
            { _traceLastPosition = Environment.TickCount64; Trace("clock"); }
            lock (_seekGate) return _seeking ? _seekTarget : _player.PlaybackSession.Position;
        }
        set
        {
            _traceUntil = Environment.TickCount64 + 8000;
            Trace($"seek-request target={value.TotalSeconds:F3}");
            var target = value < TimeSpan.Zero ? TimeSpan.Zero : value > Duration && Duration > TimeSpan.Zero ? Duration : value;
            lock (_seekGate)
            {
                if (_disposed) return;
                if (!_player.PlaybackSession.CanSeek) return;
                if (VideoWidth == 0) { _player.PlaybackSession.Position = target; return; }
                _stepAfterSeek = 0;
                if (_seeking) { _seekTarget = target; return; }
                if (Math.Abs((target - _player.PlaybackSession.Position).Ticks) <= TimeSpan.TicksPerMillisecond) return;
                // 原生 Pause/Play 是异步的。连续定位不能用尚未更新的 Paused 覆盖用户播放意图。
                _seeking = true;
                _seekTarget = _inflightTarget = target;
                // 暂停视频时钟，定位完成后再恢复；连续输入只保留最后一个目标。
                Trace("seek-sync-pause");
                _player.Pause();
                _player.PlaybackSession.Position = target;
            }
        }
    }

    private void ResetPlaybackIntent()
    {
        lock (_seekGate)
        {
            _playRequested = false;
            _seeking = false;
            _stepAfterSeek = 0;
        }
    }

    private void OnSeekCompleted(MediaPlaybackSession sender, object args)
    {
        lock (_seekGate)
        {
            if (_disposed || !_seeking) return;
            if (_seekTarget != _inflightTarget)
            {
                _inflightTarget = _seekTarget;
                Trace($"seek-latest target={_seekTarget.TotalSeconds:F3}");
                sender.Position = _seekTarget;
                return;
            }
            _seeking = false;
            if (_stepAfterSeek != 0)
            {
                StepNative(_stepAfterSeek > 0);
                _stepAfterSeek = 0;
            }
            else if (_playRequested) { _player.Play(); Trace("seek-sync-play"); }
        }
    }

    public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;
    public uint VideoWidth => _player.PlaybackSession.NaturalVideoWidth;
    public uint VideoHeight => _player.PlaybackSession.NaturalVideoHeight;
    public int Rotation
    {
        get => _player.PlaybackSession.PlaybackRotation switch
        {
            MediaRotation.Clockwise90Degrees => 90,
            MediaRotation.Clockwise180Degrees => 180,
            MediaRotation.Clockwise270Degrees => 270,
            _ => 0,
        };
        set => _player.PlaybackSession.PlaybackRotation = value switch
        {
            90 => MediaRotation.Clockwise90Degrees,
            180 => MediaRotation.Clockwise180Degrees,
            270 => MediaRotation.Clockwise270Degrees,
            _ => MediaRotation.None,
        };
    }
    public bool IsPlaying
    {
        get
        {
            lock (_seekGate) return !_disposed && _playRequested;
        }
    }

    public double Volume
    {
        get => _player.Volume * 100;
        set => _player.Volume = Math.Clamp(value, 0, 100) / 100;
    }

    public bool IsMuted { get => _player.IsMuted; set => _player.IsMuted = value; }
    public double Rate { get => _player.PlaybackSession.PlaybackRate; set => _player.PlaybackSession.PlaybackRate = value; }

    public IReadOnlyList<string> AudioTracks => _item is null ? [] : _item.AudioTracks
        .Select((track, i) => string.IsNullOrWhiteSpace(track.Label)
            ? string.IsNullOrWhiteSpace(track.Language) ? AppText.Format("AudioTrack", i + 1) : AppText.Format("AudioTrack057b50", i + 1, track.Language)
            : track.Label).ToList();

    public int AudioTrack
    {
        get => _item?.AudioTracks.SelectedIndex ?? -1;
        set { if (_item is not null && value >= 0 && value < _item.AudioTracks.Count) _item.AudioTracks.SelectedIndex = value; }
    }

    public async Task OpenAsync(string path, CancellationToken ct)
    {
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct);
        ct.ThrowIfCancellationRequested();
        _keyframesCancellation?.Cancel();
        _keyframesCancellation?.Dispose();
        _keyframesCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _keyframes = null;
        _path = path;
        _source = MediaSource.CreateFromStorageFile(file);
        _item = new MediaPlaybackItem(_source);
        _item.AudioTracksChanged += (_, _) => TracksChanged?.Invoke(this, EventArgs.Empty);
        _player.Source = _item;
    }

    public void Play()
    {
        lock (_seekGate)
        {
            if (_disposed) return;
            Trace("play-call");
            _playRequested = true;
            _stepAfterSeek = 0;
            if (!_seeking) _player.Play();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Pause()
    {
        lock (_seekGate)
        {
            if (_disposed) return;
            Trace("pause-call");
            _playRequested = false;
            _player.Pause();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // 系统播放器的逐帧要求先暂停。
    public void StepFrame(bool forward)
    {
        lock (_seekGate)
        {
            if (_disposed) return;
            Pause();
            if (_seeking) _stepAfterSeek = forward ? 1 : -1;
            else StepNative(forward);
        }
    }

    private void StepNative(bool forward)
    {
        if (forward) _player.StepForwardOneFrame();
        else _player.StepBackwardOneFrame();
    }

    public void Dispose()
    {
        lock (_seekGate)
        {
            _disposed = true; _seeking = false; _playRequested = false;
            _keyframesCancellation?.Cancel();
            _keyframesCancellation?.Dispose();
            _keyframes = null;
        }
        _player.PlaybackSession.SeekCompleted -= OnSeekCompleted;
        _player.Pause();
        _player.Source = null;
        _view.SetMediaPlayer(null);
        _player.Dispose();
        _source?.Dispose();
        _source = null;
        _item = null;
    }
}
