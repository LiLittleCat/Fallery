using System.Runtime.InteropServices;

namespace Fallery;

public sealed partial class MainWindow
{
    private NativeWindowPlacement? _browseWindowPlacement;

    private void CaptureBrowseWindowPlacement()
    {
        if (IsFilmstrip || ViewerState.IsOpen || _browseWindowPlacement is not null) return;
        RememberCurrentBrowseWindowPlacement();
    }

    private void RememberCurrentBrowseWindowPlacement()
    {
        var placement = new NativeWindowPlacement { Length = (uint)Marshal.SizeOf<NativeWindowPlacement>() };
        if (GetWindowPlacement(WinRT.Interop.WindowNative.GetWindowHandle(this), ref placement))
            _browseWindowPlacement = placement;
    }

    private void RestoreBrowseWindowPlacement()
    {
        if (_browseWindowPlacement is not { } placement) return;
        _browseWindowPlacement = null;
        if (_closed || _closing) return;
        // WINDOWPLACEMENT 同时保留普通窗口位置、大小及最大化状态，避免混用工作区和屏幕坐标导致偏移。
        if (!SetWindowPlacement(WinRT.Interop.WindowNative.GetWindowHandle(this), in placement))
            GalleryState.Status = "无法恢复浏览窗口的位置和大小";
        QueueSettingsSave();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPlacement
    {
        public uint Length, Flags, ShowCommand;
        public NativePoint MinPosition, MaxPosition;
        public NativeRect NormalPosition;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint hwnd, ref NativeWindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint hwnd, in NativeWindowPlacement placement);
}
