using System.Runtime.InteropServices;
using Fallery.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Fallery;

/// <summary>自定义入口：单实例。再次启动（含“打开方式”）时把路径交给已开着的窗口，自己退出。</summary>
public static class Program
{
    private const string InstanceKey = "Fallery.Main";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectToRunningInstance()) return 0;
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
            instance.Activated += (_, activation) => App.Current?.OnRedirected(activation);
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
