using System.Runtime.InteropServices;
using Fallery.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Fallery;

/// <summary>一个进程管理多个独立窗口；再次启动把请求转给主进程创建新窗口。</summary>
public static class Program
{
    private static readonly System.Collections.Concurrent.ConcurrentQueue<AppActivationArguments> PendingActivations = new();
    private const string InstanceKey = AppIdentity.InstanceKey;

    [STAThread]
    private static int Main(string[] args)
    {
        Velopack.VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_ => VelopackUpdates.RegisterShell())
            .OnAfterUpdateFastCallback(_ => VelopackUpdates.RegisterShell())
            .OnBeforeUninstallFastCallback(_ => VelopackUpdates.UnregisterShell())
            .Run();
        if (ShellRegistrationCommand.TryExecute(args, Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Fallery.exe"),
            FolderScanner.SupportedExtensions, out int exitCode)) return exitCode;
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectToRunningInstance()) return 0;
        AppText.SetLanguage(AppSettingsStore.Shared.LoadAsync().GetAwaiter().GetResult().Language);
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(StartupArguments.FindPath(args));
        });
        return 0;
    }

    private static bool RedirectToRunningInstance()
    {
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (instance.IsCurrent)
        {
            instance.Activated += (_, activation) =>
            {
                PendingActivations.Enqueue(activation);
                if (App.Current is { } app) DeliverPendingActivations(app);
            };
            return false;
        }
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        // 允许已开实例把窗口切到前台，否则 Windows 只会让任务栏图标闪烁。
        AllowSetForegroundWindow((int)instance.ProcessId);
        // 重定向需要在后台线程完成，主线程用 COM 可重入的等待，避免 STA 死锁。
        var done = CreateEvent(IntPtr.Zero, true, false, null);
        _ = Task.Run(() =>
        {
            try { instance.RedirectActivationToAsync(activation).AsTask().Wait(); }
            finally { SetEvent(done); }
        });
        CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [done], out _);
        CloseHandle(done);
        return true;
    }

    internal static void DeliverPendingActivations(App app)
    {
        while (PendingActivations.TryDequeue(out var activation)) app.OnRedirected(activation);
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint flags, uint timeout, uint count, IntPtr[] handles, out uint index);
}
