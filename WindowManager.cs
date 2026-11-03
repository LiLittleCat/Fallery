namespace Fallery;

/// <summary>所有窗口共用 UI 线程，导航、选择、播放器和关闭生命周期彼此独立。</summary>
public sealed class WindowManager(Func<string?, MainWindow>? factory = null, bool activateWindows = true)
{
    private readonly HashSet<MainWindow> _windows = [];
    public IReadOnlyCollection<MainWindow> Windows => _windows;
    public event Action? AllWindowsClosed;

    public MainWindow OpenWindow(string? path)
    {
        var window = factory?.Invoke(path) ?? new MainWindow(startupPath: path);
        _windows.Add(window);
        window.NewWindowRequested += OpenRequestedWindow;
        window.Closed += (_, _) =>
        {
            window.NewWindowRequested -= OpenRequestedWindow;
            _windows.Remove(window);
            if (_windows.Count == 0) AllWindowsClosed?.Invoke();
        };
        if (activateWindows) window.ActivateFromRedirect(null);
        return window;
    }

    private void OpenRequestedWindow(string? path) => OpenWindow(path);
}
