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

    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event EventHandler? StateChanged;
    public event EventHandler<string>? Failed;
    public event EventHandler? TracksChanged;

    public WindowsMediaPlayer(MediaPlayerElement view, bool enableSystemTransportControls = true)
    {
        _view = view;
        if (!enableSystemTransportControls) _player.CommandManager.IsEnabled = false;
        _player.MediaOpened += (_, _) => Opened?.Invoke(this, EventArgs.Empty);
        _player.MediaEnded += (_, _) => Ended?.Invoke(this, EventArgs.Empty);
        _player.MediaFailed += (_, args) => Failed?.Invoke(this, args.ErrorMessage);
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _view.SetMediaPlayer(_player);
    }

    public TimeSpan Position
    {
        get => _player.PlaybackSession.Position;
        set => _player.PlaybackSession.Position = value < TimeSpan.Zero ? TimeSpan.Zero : value > Duration && Duration > TimeSpan.Zero ? Duration : value;
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
    public bool IsPlaying => _player.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Opening or MediaPlaybackState.Buffering;

    public double Volume
    {
        get => _player.Volume * 100;
        set => _player.Volume = Math.Clamp(value, 0, 100) / 100;
    }

    public bool IsMuted { get => _player.IsMuted; set => _player.IsMuted = value; }
    public double Rate { get => _player.PlaybackSession.PlaybackRate; set => _player.PlaybackSession.PlaybackRate = value; }

    public IReadOnlyList<string> AudioTracks => _item is null ? [] : _item.AudioTracks
        .Select((track, i) => string.IsNullOrWhiteSpace(track.Label)
            ? string.IsNullOrWhiteSpace(track.Language) ? $"音轨 {i + 1}" : $"音轨 {i + 1}（{track.Language}）"
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
        _source = MediaSource.CreateFromStorageFile(file);
        _item = new MediaPlaybackItem(_source);
        _item.AudioTracksChanged += (_, _) => TracksChanged?.Invoke(this, EventArgs.Empty);
        _player.Source = _item;
    }

    public void Play() => _player.Play();
    public void Pause() => _player.Pause();

    // 系统播放器的逐帧要求先暂停。
    public void StepFrame(bool forward)
    {
        _player.Pause();
        if (forward) _player.StepForwardOneFrame();
        else _player.StepBackwardOneFrame();
    }

    public void Dispose()
    {
        _player.Pause();
        _player.Source = null;
        _view.SetMediaPlayer(null);
        _player.Dispose();
        _source?.Dispose();
        _source = null;
        _item = null;
    }
}
