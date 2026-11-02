using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Dispatching;

namespace Fallery;

public partial class App : Application
{
    private readonly WindowManager _windows = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly string? _startupPath;
    internal bool IsUpdating { get; private set; }
    public static new App? Current => Application.Current as App;

    public App(string? startupPath = null)
    {
        _startupPath = startupPath;
        UnhandledException += (_, e) =>
        {
            try
            {
                string folder = AppIdentity.DataDirectory;
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:O}\n{e.Message}\n{e.Exception}\n\n");
            }
            catch { /* 诊断写入失败不能掩盖原异常。 */ }
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        OpenWindow(_startupPath);
        Program.DeliverPendingActivations(this);
    }

    internal MainWindow OpenWindow(string? path) => _windows.OpenWindow(path);

    internal Task RefreshFolderExclusionsAsync() =>
        Task.WhenAll(_windows.Windows.ToArray().Select(window => window.RefreshFolderExclusionsAsync()));

    internal async Task ApplyUpdateAsync(MainWindow requestingWindow)
    {
        if (IsUpdating) return;
        var windows = _windows.Windows.ToArray();
        await VelopackUpdates.Shared.ApplyAsync(async () =>
        {
            if (windows.Any(window => !window.CanPrepareUpdate(ReferenceEquals(window, requestingWindow))))
                throw new InvalidOperationException(AppText.Get("AnotherWindowIsWorkingWithFilesOrClosingPlease"));
            IsUpdating = true;
            foreach (var window in windows) window.BeginUpdateShutdown();
            foreach (var window in windows) await window.SaveBeforeUpdateAsync();
            await UpdateDataBackup.SaveAsync(AppIdentity.DataDirectory);
        }, () =>
        {
            foreach (var window in windows) window.CloseForUpdate();
        });
        if (IsUpdating && VelopackUpdates.Shared.Status.State != AppUpdateState.Applying)
        {
            foreach (var window in windows) window.CancelUpdateShutdown();
            IsUpdating = false;
        }
    }

    /// <summary>另一个进程转来的启动请求；回调在后台线程，切回窗口线程处理。</summary>
    internal void OnRedirected(AppActivationArguments activation)
    {
        string? path = activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
            ? StartupArguments.FindPath(launch.Arguments) : null;
        _dispatcher.TryEnqueue(() => { if (!IsUpdating) OpenWindow(path); });
    }
}
