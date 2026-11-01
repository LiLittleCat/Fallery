using System.Reflection;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunMultiWindowChecksAsync(string workspace)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        object? Get(MainWindow window, string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        string firstPath = Path.Combine(workspace, "work", "validation", "experience", "01-图片");
        string secondPath = Path.Combine(workspace, "work", "validation", "experience", "10-混合");
        var store = new AppSettingsStore(Path.Combine(workspace, "work", "validation", "multi-window", "settings.json"));
        await store.SaveAsync(new AppSettings { Muted = true, Volume = 0, VideoWindowDefaultsApplied = true });
        var manager = new WindowManager(path => new MainWindow(store, startupPath: path, monitorFolderChanges: false), activateWindows: false);
        async Task Init(MainWindow window, string folder)
        {
            // 未激活的窗口不能显示 Slider 的原生数值浮层，与主测试宿主采用相同设置。
            ((Microsoft.UI.Xaml.Controls.Slider)((FrameworkElement)window.Content).FindName("ThumbSize")).IsThumbToolTipEnabled = false;
            typeof(MainWindow).GetField("_initialized", flags)!.SetValue(window, true);
            var bindings = Get(window, "Bindings")!;
            bindings.GetType().GetMethod("Initialize")!.Invoke(bindings, null);
            await (Task)Call(window, "RestoreSettingsAsync")!;
            var root = (FrameworkElement)window.Content;
            root.Measure(new(1000, 700)); root.Arrange(new(0, 0, 1000, 700)); root.UpdateLayout();
            await (Task)Call(window, "NavigateAsync", folder, false, false)!;
        }
        async Task Close(MainWindow window)
        {
            var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult();
            window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        try
        {
            var first = manager.OpenWindow(firstPath);
            await Init(first, firstPath);
            Call(first, "RequestNewWindow", secondPath);
            var second = manager.Windows.Single(window => window != first);
            await Init(second, secondPath);
            Check(manager.Windows.Count == 2 && Get(second, "_startupPath") as string == secondPath, "新窗口请求应由生产窗口管理器创建独立目标窗口");
            Check(first.GalleryState.CurrentFolder == firstPath && second.GalleryState.CurrentFolder == secondPath
                && !ReferenceEquals(first.GalleryState, second.GalleryState) && !ReferenceEquals(first.ViewerState, second.ViewerState),
                "两个窗口的目录与查看器状态必须独立");
            Call(first, "SelectItem", first.GalleryState.Items[0]);
            Check(Get(second, "_selectedItem") is null, "选择不能跨窗口串联");
            Call(first, "EnsureSearchIndex"); Call(second, "EnsureSearchIndex");
            Check(ReferenceEquals(Get(first, "_searchIndex"), Get(second, "_searchIndex"))
                && ReferenceEquals(Get(first, "_resumeStore"), Get(second, "_resumeStore")), "多个窗口应共享索引与续播存储，避免互相覆盖");
            var version = new FileVersion(@"C:\multi-window-test.jpg", 10, 1);
            var bitmap = new BitmapImage(); MediaCache.StoreThumbnail(version, 128, bitmap);
            await Close(first);
            Check(manager.Windows.Count == 1 && manager.Windows.Contains(second)
                && ReferenceEquals(MediaCache.GetThumbnail(version, 128), bitmap), "关闭首窗口不能关闭其他窗口或清掉共用缩略图");
            Call(second, "RequestNewWindow", firstPath);
            Check(manager.Windows.Count == 2, "首窗口关闭后仍能从剩余窗口创建新窗口");
            await (Task)Call(second, "NavigateAsync", firstPath, false, false)!;
            Check(second.GalleryState.CurrentFolder == firstPath, "首窗口关闭后剩余窗口仍能导航");
            await Close(second);
            Check(manager.Windows.Count == 1, "关闭第二个窗口不能结束后来打开的窗口");
        }
        finally { foreach (var window in manager.Windows.ToArray()) await Close(window); }
        return checks;
    }
}
