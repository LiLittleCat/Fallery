using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Fallery;

public partial class App : Application
{
    private MainWindow? _window;
    private readonly string? _startupPath;
    public static new App? Current => Application.Current as App;

    public App(string? startupPath = null)
    {
        _startupPath = startupPath;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(startupPath: _startupPath);
        _window.Activate();
    }

    /// <summary>另一个进程转来的启动请求；回调在后台线程，切回窗口线程处理。</summary>
    internal void OnRedirected(AppActivationArguments activation)
    {
        string? path = activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
            ? StartupArguments.FindPath(launch.Arguments) : null;
        _window?.DispatcherQueue.TryEnqueue(() => _window.ActivateFromRedirect(path));
    }
}
