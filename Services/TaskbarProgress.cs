using System.Runtime.InteropServices;

namespace Fallery.Services;

/// <summary>每个窗口独立使用 Windows 任务栏进度；只能在窗口 UI 线程调用。</summary>
internal sealed class TaskbarProgress(nint window) : IDisposable
{
    private ITaskbarList3? _taskbar;
    private bool _disposed;
    private long _retryAfter;

    public bool Update(TimeSpan position, TimeSpan duration, bool playing)
    {
        if (duration <= TimeSpan.Zero) return Clear();
        return Apply(taskbar =>
        {
            CheckResult(taskbar.SetProgressValue(window,
                (ulong)Math.Clamp(position.Ticks, 0, duration.Ticks), (ulong)duration.Ticks));
            // 暂停态不会被 SetProgressValue 自动清除，恢复播放须显式设 NORMAL。
            CheckResult(taskbar.SetProgressState(window, playing ? 2u : 8u));
        });
    }

    public bool Clear() => _taskbar is null || Apply(taskbar =>
        CheckResult(taskbar.SetProgressState(window, 0)));

    private static void CheckResult(int result)
    {
        // 不让 .NET 将某些 HRESULT 映射为 NotImplementedException 等并逃出 COM 回退路径。
        if (result < 0) throw new COMException("Taskbar progress is unavailable.", result);
    }

    private bool Apply(Action<ITaskbarList3> action)
    {
        if (_disposed || Environment.TickCount64 < _retryAfter) return false;
        try
        {
            if (_taskbar is null)
            {
                var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"), throwOnError: true)!;
                _taskbar = (ITaskbarList3)Activator.CreateInstance(type)!;
                CheckResult(_taskbar.HrInit());
            }
            action(_taskbar);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException or NotImplementedException)
        {
            Release();
            // Explorer 暂不可用时不打断播放，稍后重建连接。
            _retryAfter = Environment.TickCount64 + 5000;
            return false;
        }
    }

    private void Release()
    {
        if (_taskbar is not null) Marshal.ReleaseComObject(_taskbar);
        _taskbar = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
        Release();
    }

    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(nint hwnd);
        [PreserveSig] int DeleteTab(nint hwnd);
        [PreserveSig] int ActivateTab(nint hwnd);
        [PreserveSig] int SetActiveAlt(nint hwnd);
        [PreserveSig] int MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        [PreserveSig] int SetProgressValue(nint hwnd, ulong completed, ulong total);
        [PreserveSig] int SetProgressState(nint hwnd, uint state);
    }
}
