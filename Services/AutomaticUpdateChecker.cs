namespace Fallery.Services;

/// <summary>进程只启动一次；延后首查避开启动加载，失败留待下次检查。</summary>
public sealed class AutomaticUpdateChecker(AppUpdateService service, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private CancellationTokenSource? _lifetime;
    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Start()
    {
        if (_lifetime is not null || service.Status.State == AppUpdateState.Disabled) return;
        _lifetime = new();
        Completion = RunAsync(_lifetime.Token);
    }

    public void Stop() => _lifetime?.Cancel();

    private async Task RunAsync(CancellationToken ct)
    {
        var wait = delay ?? ((TimeSpan duration, CancellationToken token) => Task.Delay(duration, token));
        try
        {
            await wait(TimeSpan.FromSeconds(10), ct);
            while (!ct.IsCancellationRequested)
            {
                await service.CheckInBackgroundAsync(ct);
                await wait(TimeSpan.FromHours(6), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
