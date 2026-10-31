namespace Fallery.Services.Playback;

/// <summary>
/// 播放内核的统一接口。窗口只依赖它；当前只有系统播放器实现，以后更换内核时新增实现即可。
/// 事件可能在后台线程触发，调用方负责切回界面线程。
/// </summary>
public interface IPlayer : IDisposable
{
    event EventHandler? Opened;
    event EventHandler? Ended;
    event EventHandler? StateChanged;
    event EventHandler<string>? Failed;
    event EventHandler? TracksChanged;

    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }
    bool IsPlaying { get; }
    /// <summary>0–100。</summary>
    double Volume { get; set; }
    bool IsMuted { get; set; }
    double Rate { get; set; }
    IReadOnlyList<string> AudioTracks { get; }
    int AudioTrack { get; set; }

    Task OpenAsync(string path, CancellationToken ct);
    void Play();
    void Pause();
    void StepFrame(bool forward);
}
