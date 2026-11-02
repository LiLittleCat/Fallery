namespace Fallery.Services;

public enum AppUpdateState { Disabled, Idle, Checking, Available, Downloading, Ready, Applying, Latest, Failed }

public sealed record AppUpdateRelease(string Version, string Notes, object Payload);
public sealed record AppUpdateStatus(AppUpdateState State, string Message, int Progress = 0, AppUpdateRelease? Release = null)
{
    public string Language { get; } = AppText.Language;
    public string DisplayMessage => AppText.TranslateStatus(Message, Language);
}

public interface IAppUpdateBackend
{
    AppUpdateRelease? Pending { get; }
    Task<AppUpdateRelease?> CheckAsync(CancellationToken ct);
    Task DownloadAsync(AppUpdateRelease release, Action<int> progress, CancellationToken ct);
    void ScheduleRestart(AppUpdateRelease release);
}

/// <summary>一个进程的所有窗口共享更新状态，下载成功前不允许替换程序。</summary>
public sealed class AppUpdateService(IAppUpdateBackend? backend, string disabledReason = "")
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    private CancellationTokenSource? _cancellation;
    public event Action? Changed;
    public AppUpdateStatus Status { get; private set; } = backend is null
        ? new(AppUpdateState.Disabled, disabledReason)
        : backend.Pending is { } pending ? new(AppUpdateState.Ready, AppText.Get("UpdateDownloadedYouCanRestartToInstallIt"), 100, pending)
        : new(AppUpdateState.Idle, AppText.Get("CheckManuallyForANewVersion"));

    private void Set(AppUpdateStatus value)
    {
        Status = value;
        Changed?.Invoke();
    }

    public void Cancel()
    {
        try { _cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public async Task CheckAsync()
    {
        if (backend is null || !await _operation.WaitAsync(0)) return;
        try
        {
            if (Status.State == AppUpdateState.Ready) return;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            _cancellation = cancellation;
            Set(new(AppUpdateState.Checking, AppText.Get("CheckingForUpdates")));
            var release = await backend.CheckAsync(cancellation.Token);
            Set(release is null ? new(AppUpdateState.Latest, AppText.Get("YouAreUpToDate"))
                : new(AppUpdateState.Available, AppText.Format("VersionIsAvailable", release.Version), Release: release));
        }
        catch (OperationCanceledException) { Set(new(AppUpdateState.Idle, AppText.Get("UpdateCheckCanceledOrTimedOutYouCanTry"))); }
        catch (Exception ex) { Set(new(AppUpdateState.Failed, DescribeError(ex))); }
        finally { _cancellation = null; _operation.Release(); }
    }

    public async Task DownloadAsync()
    {
        if (backend is null || !await _operation.WaitAsync(0)) return;
        var release = Status.Release;
        try
        {
            if (release is null || Status.State is not AppUpdateState.Available) return;
            using var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            Set(new(AppUpdateState.Downloading, AppText.Get("DownloadingUpdate"), Release: release));
            await backend.DownloadAsync(release, value =>
            {
                if (!cancellation.IsCancellationRequested && Status.State == AppUpdateState.Downloading)
                    Set(new(AppUpdateState.Downloading, AppText.Format("DownloadingUpdate09edc4", Math.Clamp(value, 0, 100)), Math.Clamp(value, 0, 100), release));
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Set(new(AppUpdateState.Ready, AppText.Get("UpdateDownloadedAndVerifiedRestartToInstall"), 100, release));
        }
        catch (OperationCanceledException) { Set(new(AppUpdateState.Available, AppText.Get("DownloadCanceledYouCanDownloadAgain"), Release: release)); }
        catch (Exception ex) { Set(new(AppUpdateState.Available, DescribeError(ex), Release: release)); }
        finally { _cancellation = null; _operation.Release(); }
    }

    public async Task ApplyAsync(Func<Task> prepare, Action closeWindows)
    {
        if (backend is null || !await _operation.WaitAsync(0)) return;
        var release = Status.Release;
        try
        {
            if (Status.State != AppUpdateState.Ready || release is null) return;
            Set(new(AppUpdateState.Applying, AppText.Get("SavingAndPreparingToRestart"), 100, release));
            await prepare();
            // 更新器先等待进程正常退出，不能在窗口保存完成前强行结束进程。
            backend.ScheduleRestart(release);
            closeWindows();
        }
        catch (Exception ex) { Set(new(AppUpdateState.Ready, AppText.Format("CouldNotStartUpdate", ex.Message), 100, release)); }
        finally { _operation.Release(); }
    }

    private static string DescribeError(Exception ex)
    {
        if (ex is HttpRequestException http && http.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            return AppText.Get("CannotAccessTheUpdateSourceTheRepositoryMayBe");
        return AppText.Format("UpdateFailedTryAgainLater", ex.Message);
    }
}
