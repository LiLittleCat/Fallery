using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Fallery.Services.Playback;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Fallery;

public sealed partial class MainWindow
{
    private static readonly double[] PlaybackRates = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3];
    private readonly ResumeStore _resumeStore = new(ResumeStore.DefaultPath);
    private DispatcherQueueTimer? _videoTimer;
    private MediaItem? _videoItem;
    private bool _updatingSeek;
    private bool _userSeeking;
    private double _volume = 100;
    private bool _muted;
    private bool _playlistOpen;
    private bool _playlistShowThumbnails = true;
    private DataTemplate? _playlistThumbnailTemplate;
    private double _rate = 1;
    private TimeSpan? _loopStart;
    private TimeSpan? _loopEnd;

    private void InitializePlayback()
    {
        _playlistThumbnailTemplate = PlaylistView.ItemTemplate;
        _videoTimer = DispatcherQueue.CreateTimer();
        _videoTimer.Interval = TimeSpan.FromMilliseconds(250);
        _videoTimer.Tick += (_, _) => UpdateVideoProgress();
        // 拖动进度条期间不让定时刷新把滑块拉回旧位置。
        VideoSeek.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _userSeeking = true), true);
        VideoSeek.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => _userSeeking = false), true);
        VideoSeek.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => _userSeeking = false), true);
        ViewerState.PropertyChanged += (_, args) =>
        {
            if (_closed) return;
            if (args.PropertyName is nameof(ViewerState.Playlist)) PlaylistView.ItemsSource = ViewerState.Playlist;
            if (args.PropertyName is nameof(ViewerState.CurrentIndex) or nameof(ViewerState.Playlist)) SyncPlaylistSelection();
        };
        foreach (var rate in PlaybackRates)
        {
            var entry = new RadioMenuFlyoutItem
            {
                Text = $"{rate:0.##}×", Tag = rate, GroupName = "PlaybackRate",
                FontSize = 13, MinWidth = 0, MinHeight = 30, Padding = new Thickness(6, 4, 6, 4)
            };
            entry.Click += VideoRate_Click;
            VideoRateMenu.Items.Add(entry);
        }
        UpdateRateLabel();
    }

    /// <summary>创建播放内核并接好界面；以后更换内核只替换这里的实现类。</summary>
    private IPlayer CreatePlayer(MediaItem item, CancellationToken ct)
    {
        IPlayer player = CreatePlayer(ViewerVideo, enableSystemTransportControls: true);
        _videoItem = item;
        _loopStart = _loopEnd = null;
        player.Volume = _volume;
        player.IsMuted = _muted;
        player.Rate = _rate;
        player.Opened += (_, _) => DispatcherQueue.TryEnqueue(() => _ = PrepareVideoPlaybackAsync(player, item, ct));
        player.Failed += (_, message) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || !ReferenceEquals(_videoPlayer, player)) return;
            ViewerState.Message = UnsupportedVideoMessage(item.Path, message);
            StopVideo();
        });
        player.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed && ReferenceEquals(_videoPlayer, player)) UpdatePlayIcon(player.IsPlaying);
        });
        player.TracksChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed && ReferenceEquals(_videoPlayer, player)) UpdateTrackMenu();
        });
        player.Ended += (_, _) => DispatcherQueue.TryEnqueue(async () =>
        {
            if (_closed || !ReferenceEquals(_videoPlayer, player)) return;
            UpdatePlayIcon(false);
            if (item.Version is { } version) _ = _resumeStore.SaveAsync(version, TimeSpan.Zero, player.Duration);
            // 下一项也是视频时连续播放；图片不自动切换，避免看完视频被带走。
            int next = ViewerState.CurrentIndex + 1;
            if (ViewerState.CanNext && ViewerState.Playlist[next].IsVideo) await ShowInViewerAsync(next);
        });
        _updatingSeek = true;
        VideoSeek.Value = 0;
        VideoSeek.Maximum = 1;
        _updatingSeek = false;
        VideoPositionText.Text = VideoDurationText.Text = FormatTime(TimeSpan.Zero);
        UpdatePlayIcon(true);
        UpdateMuteIcon();
        UpdateLoopLabel();
        UpdateTrackMenu();
        _videoTimer?.Start();
        return player;
    }

    private async Task PrepareVideoPlaybackAsync(IPlayer player, MediaItem item, CancellationToken ct)
    {
        try
        {
            if (_closed || ct.IsCancellationRequested || !ReferenceEquals(_videoPlayer, player)) return;
            FitVideoWindowButton.IsEnabled = player.VideoWidth > 0 && player.VideoHeight > 0;
            if (player.VideoWidth > 0 && player.VideoHeight > 0)
                VideoSurface.AspectRatio = (double)player.VideoWidth / player.VideoHeight;
            player.Rate = _rate;
            UpdateTrackMenu();
            if (item.Version is { } version && await _resumeStore.GetAsync(version) is { } resume
                && !ct.IsCancellationRequested && ReferenceEquals(_videoPlayer, player) && resume < player.Duration)
            {
                player.Position = resume;
                GalleryState.Status = $"从 {FormatTime(resume)} 继续播放";
            }
            ct.ThrowIfCancellationRequested();
            // 控制栏和画面区域先完成布局，再露出视频；保持进入前的窗口尺寸。
            Root.UpdateLayout();
            await WaitForVideoLayoutFrameAsync(ct);
            Root.UpdateLayout();
            await WaitForVideoLayoutFrameAsync(ct);
            if (_closed || !ReferenceEquals(_videoPlayer, player)) return;
            ViewerState.Message = "";
            ViewerVideo.Opacity = 1;
            ViewerVideo.IsHitTestVisible = true;
            RotateMediaButton.IsEnabled = player.VideoWidth > 0 && player.VideoHeight > 0;
            player.Play();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && ReferenceEquals(_videoPlayer, player))
            {
                ViewerState.Message = $"无法准备视频画面：{ex.Message}";
                StopVideo();
            }
        }
    }

    private static async Task WaitForVideoLayoutFrameAsync(CancellationToken ct)
    {
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRendering(object? sender, object args) => frame.TrySetResult();
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
        try { await frame.Task.WaitAsync(ct); }
        finally { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering; }
    }

    /// <summary>停止前记下进度，下次打开同一文件从这里继续。</summary>
    private void DetachVideoControls(IPlayer? player)
    {
        _videoTimer?.Stop();
        _userSeeking = false;
        if (player is not null && _videoItem?.Version is { } version && player.Duration > TimeSpan.Zero)
            _ = _resumeStore.SaveAsync(version, player.Position, player.Duration);
        _videoItem = null;
    }

    private void UpdateVideoProgress()
    {
        if (_videoPlayer is not { } player) return;
        var duration = player.Duration;
        var position = player.Position;
        // A-B 循环：到达 B 点跳回 A 点。
        if (_loopStart is { } a && _loopEnd is { } b && b > a && position >= b)
        {
            player.Position = a;
            position = a;
        }
        VideoDurationText.Text = FormatTime(duration);
        VideoPositionText.Text = FormatTime(position);
        if (_userSeeking || duration <= TimeSpan.Zero) return;
        _updatingSeek = true;
        VideoSeek.Maximum = duration.TotalSeconds;
        VideoSeek.Value = Math.Min(position.TotalSeconds, duration.TotalSeconds);
        _updatingSeek = false;
    }

    /// <summary>
    /// 系统播放器依赖 Windows 已安装的解码器：HEVC、AV1 需要 Microsoft Store 的扩展，MKV 能否播放取决于其中的编码。
    /// </summary>
    private static string UnsupportedVideoMessage(string path, string detail)
    {
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        string hint = extension == ".mkv"
            ? "MKV 中的 HEVC、AV1 等编码需要系统扩展，当前版本暂不支持。"
            : "如果是 HEVC（H.265）或 AV1 编码，需要从 Microsoft Store 安装“HEVC 视频扩展”或“AV1 视频扩展”，当前版本暂不内置这些解码器。";
        return $"无法播放此视频。{hint}\n{detail}";
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    private void UpdatePlayIcon(bool playing)
    {
        VideoPlayIcon.Glyph = FluentIcons.Glyph(playing ? "IconPauseMedia" : "IconPlayMedia");
        // 播放三角的视觉重心偏左，右移一点才显得居中。
        VideoPlayIcon.Margin = new Thickness(playing ? 0 : 1, 0, 0, 0);
    }

    private void UpdateMuteIcon()
    {
        bool silent = _muted || _volume == 0;
        VideoMuteIcon.Glyph = FluentIcons.Glyph(silent ? "IconMute" : "IconVolume");
        string label = _muted ? "取消静音 (M)" : "静音 (M)";
        ToolTipService.SetToolTip(VideoMuteButton, label);
    }

    private void ToggleVideoPlayback()
    {
        if (ViewerVideo.Opacity < 1 || _videoPlayer is not { } player) return;
        if (player.IsPlaying) player.Pause();
        else
        {
            // 播完后再次播放从头开始。
            if (player.Duration > TimeSpan.Zero && player.Position >= player.Duration - TimeSpan.FromMilliseconds(200))
                player.Position = _loopStart ?? TimeSpan.Zero;
            player.Play();
        }
    }

    /// <summary>与 PotPlayer 一致：播放视频时 ←/→ 快退/快进。</summary>
    private void SeekVideo(TimeSpan delta)
    {
        if (_videoPlayer is not { } player || player.Duration <= TimeSpan.Zero) return;
        player.Position += delta;
        UpdateVideoProgress();
    }

    private void StepVideoFrame(bool forward)
    {
        if (_videoPlayer is not { } player) return;
        player.StepFrame(forward);
        UpdatePlayIcon(false);
        UpdateVideoProgress();
    }

    private void ChangeVolume(double delta) => VideoVolume.Value = Math.Clamp(VideoVolume.Value + delta, 0, 100);

    private void ToggleMute()
    {
        _muted = !_muted;
        if (_videoPlayer is not null) _videoPlayer.IsMuted = _muted;
        UpdateMuteIcon();
        QueueSettingsSave();
    }

    /// <summary>倍速：step 为 ±1 档，0 恢复正常速度。</summary>
    private void ChangeRate(int step)
    {
        int index = Array.IndexOf(PlaybackRates, _rate);
        if (index < 0) index = Array.IndexOf(PlaybackRates, 1.0);
        _rate = step == 0 ? 1 : PlaybackRates[Math.Clamp(index + step, 0, PlaybackRates.Length - 1)];
        if (_videoPlayer is not null) _videoPlayer.Rate = _rate;
        UpdateRateLabel();
        GalleryState.Status = $"播放速度 {_rate:0.##}×";
    }

    private void UpdateRateLabel()
    {
        VideoRateText.Text = $"{_rate:0.##}×";
        foreach (var entry in VideoRateMenu.Items.OfType<RadioMenuFlyoutItem>())
            entry.IsChecked = entry.Tag is double rate && rate == _rate;
    }

    private void SetLoopPoint(bool start)
    {
        if (_videoPlayer is not { } player) return;
        if (start) _loopStart = player.Position;
        else _loopEnd = player.Position;
        if (_loopStart is { } a && _loopEnd is { } b && b <= a) _loopEnd = null;
        UpdateLoopLabel();
        GalleryState.Status = _loopStart is { } s && _loopEnd is { } e ? $"A-B 循环 {FormatTime(s)} – {FormatTime(e)}" : $"已设置 {(start ? "A" : "B")} 点";
    }

    private void ClearLoop()
    {
        _loopStart = _loopEnd = null;
        UpdateLoopLabel();
        GalleryState.Status = "已取消 A-B 循环";
    }

    private void UpdateLoopLabel()
    {
        bool active = _loopStart is not null || _loopEnd is not null;
        VideoLoopText.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        VideoLoopText.Text = $"{(_loopStart is { } a ? FormatTime(a) : "A")} – {(_loopEnd is { } b ? FormatTime(b) : "B")}";
    }

    private void UpdateTrackMenu()
    {
        VideoTrackMenu.Items.Clear();
        var tracks = _videoPlayer?.AudioTracks ?? [];
        int selected = _videoPlayer?.AudioTrack ?? -1;
        for (int i = 0; i < tracks.Count; i++)
        {
            int index = i;
            var entry = new RadioMenuFlyoutItem { Text = tracks[i], GroupName = "AudioTrack", IsChecked = i == selected };
            entry.Click += (_, _) => { if (_videoPlayer is not null) _videoPlayer.AudioTrack = index; };
            VideoTrackMenu.Items.Add(entry);
        }
        VideoTrackMenu.IsEnabled = tracks.Count > 1;
        if (tracks.Count == 0) VideoTrackMenu.Items.Add(new MenuFlyoutItem { Text = "无可选音轨", IsEnabled = false });
    }

    /// <summary>按原始分辨率截取当前画面，保存到视频所在目录。</summary>
    private async Task CaptureVideoFrameAsync()
    {
        if (_videoPlayer is not { } player || _videoItem is not { } item) return;
        var time = player.Position;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.Path);
            using var frame = await VideoThumbnail.GetFrameAsync(file, time, CancellationToken.None);
            var folder = await file.GetParentAsync() ?? throw new IOException("无法访问视频所在目录");
            var name = $"{System.IO.Path.GetFileNameWithoutExtension(item.Path)}_{time.ToString(@"hh\-mm\-ss\.fff")}.png";
            var target = await folder.CreateFileAsync(name, CreationCollisionOption.GenerateUniqueName);
            var decoder = await BitmapDecoder.CreateAsync(frame);
            using var bitmap = await decoder.GetSoftwareBitmapAsync();
            using var output = await target.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            GalleryState.Status = $"已保存截图：{target.Name}";
        }
        catch (Exception ex)
        {
            if (!_closed) GalleryState.Status = $"截图失败：{ex.Message}";
        }
    }

    private void VideoPlay_Click(object sender, RoutedEventArgs e) => ToggleVideoPlayback();
    private void VideoMute_Click(object sender, RoutedEventArgs e) => ToggleMute();
    private void ViewerVideo_Tapped(object sender, TappedRoutedEventArgs e) => ToggleVideoPlayback();

    private void ViewerVideo_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // 双击的第一下已经切换过播放状态，这里切回来，只执行全屏。
        ToggleVideoPlayback();
        SetViewerFullScreen(_presenterBeforeFullScreen is null);
    }

    private void VideoRate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: double rate }) return;
        _rate = rate;
        if (_videoPlayer is not null) _videoPlayer.Rate = rate;
        UpdateRateLabel();
    }

    private void VideoStepBack_Click(object sender, RoutedEventArgs e) => StepVideoFrame(false);
    private void VideoStepForward_Click(object sender, RoutedEventArgs e) => StepVideoFrame(true);
    private void VideoLoopStart_Click(object sender, RoutedEventArgs e) => SetLoopPoint(true);
    private void VideoLoopEnd_Click(object sender, RoutedEventArgs e) => SetLoopPoint(false);
    private void VideoLoopClear_Click(object sender, RoutedEventArgs e) => ClearLoop();
    private async void VideoScreenshot_Click(object sender, RoutedEventArgs e) => await CaptureVideoFrameAsync();

    private void VideoSeek_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSeek || _videoPlayer is not { } player || player.Duration <= TimeSpan.Zero) return;
        player.Position = TimeSpan.FromSeconds(e.NewValue);
        VideoPositionText.Text = FormatTime(player.Position);
    }

    private void VideoVolume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _volume = e.NewValue;
        if (_volume > 0 && _muted) _muted = false;
        if (_videoPlayer is not null)
        {
            _videoPlayer.Volume = _volume;
            _videoPlayer.IsMuted = _muted;
        }
        UpdateMuteIcon();
        QueueSettingsSave();
    }

    private void PlaylistToggle_Click(object sender, RoutedEventArgs e) => SetPlaylistOpen(!_playlistOpen);

    private void SetPlaylistOpen(bool open)
    {
        _playlistOpen = open;
        if (open) _detailsOpen = false;
        ApplyViewerSidePanel();
        QueueSettingsSave();
    }

    private void ApplyPlaylistLayout() => ApplyViewerSidePanel();

    private void SyncPlaylistSelection()
    {
        if (PlaylistPanel.Visibility != Visibility.Visible) return;
        if (!ReferenceEquals(PlaylistView.ItemsSource, ViewerState.Playlist)) PlaylistView.ItemsSource = ViewerState.Playlist;
        PlaylistView.SelectedIndex = ViewerState.CurrentIndex;
        if (ViewerState.CurrentItem is not { } item) return;
        // 面板展开或切换行高后再定位，列表末尾不足一屏时由 ScrollViewer 限制偏移。
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_closed || PlaylistPanel.Visibility != Visibility.Visible || !ReferenceEquals(ViewerState.CurrentItem, item)) return;
            PlaylistView.UpdateLayout();
            PlaylistView.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
        });
    }

    private void PlaylistThumbnails_Click(object sender, RoutedEventArgs e)
    {
        SetPlaylistThumbnails(PlaylistThumbnailsToggle.IsChecked == true);
        QueueSettingsSave();
    }

    private void SetPlaylistThumbnails(bool show)
    {
        _playlistShowThumbnails = show;
        PlaylistThumbnailsToggle.IsChecked = show;
        string label = show ? "隐藏缩略图" : "显示缩略图";
        ToolTipService.SetToolTip(PlaylistThumbnailsToggle, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PlaylistThumbnailsToggle, label);
        var template = show ? _playlistThumbnailTemplate : (DataTemplate)Root.Resources["PlaylistTextTemplate"];
        if (template is not null && !ReferenceEquals(PlaylistView.ItemTemplate, template)) PlaylistView.ItemTemplate = template;
        SyncPlaylistSelection();
    }

    private async void PlaylistView_ItemClick(object sender, ItemClickEventArgs e)
    {
        for (int i = 0; i < ViewerState.Playlist.Count; i++)
        {
            if (!ReferenceEquals(ViewerState.Playlist[i], e.ClickedItem)) continue;
            await ShowInViewerAsync(i);
            return;
        }
    }

    private void PlaylistView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // 缩略图与浏览区共用模型和内存缓存；列表只请求小尺寸，已有更大的图会直接复用。
        if (_playlistShowThumbnails && !args.InRecycleQueue && args.Item is MediaItem item) _ = item.EnsureThumbnailAsync(160);
    }

    /// <summary>视频的全部控制都在画面下方；画面两侧的切换箭头只给图片使用。</summary>
    private void UpdateViewerOverlays()
    {
        bool video = ViewerState.CurrentItem?.IsVideo == true;
        bool fullScreen = _presenterBeforeFullScreen is not null;
        // 视频的上一项/下一项在进度条旁，画面上不叠加箭头；底栏右侧图片显示缩放、视频显示音量与倍速。
        PrevButton.Visibility = NextButton.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
        VideoControls.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        VideoAudioPanel.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        ImageZoomPanel.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
        RotateImageLeftButton.Visibility = FlipImageToggle.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
        FitVideoWindowButton.IsEnabled = video && _videoPlayer is { VideoWidth: > 0, VideoHeight: > 0 };
        ViewerFullScreenIcon.Glyph = FluentIcons.Glyph(fullScreen ? "IconExitFullScreen" : "IconFullScreen");
        ApplyFullScreenControls();
    }
}
