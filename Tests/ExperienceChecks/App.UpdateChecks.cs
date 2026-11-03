using System.Reflection;
using Fallery.Services;
using Fallery.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunUpdateWindowChecksAsync(MainWindow window)
    {
        int checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string method, params object?[] args) => typeof(MainWindow).GetMethod(method, flags)!.Invoke(window, args);
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        var root = (FrameworkElement)window.Content;
        var section = (StackPanel)Call("BuildUpdateSection")!;
        Check(section.Children.OfType<TextBlock>().Any(text => text.Text.Contains(AppIdentity.Version)), "设置显示实际版本");
        Check(section.Children.OfType<TextBlock>().Any(text => text.Text.Contains("Fallery.Dev")), "设置显示隔离后的开发数据目录");
        Check(section.Children.OfType<StackPanel>().SelectMany(panel => panel.Children).OfType<Button>()
            .Any(button => Equals(button.Content, "检查更新") && !button.IsEnabled), "开发版禁用更新请求");
        Check(((InfoBadge)root.FindName("SettingsUpdateDot")).Visibility == Visibility.Collapsed, "开发版设置入口不误报更新提示");
        var settings = (ContentDialog)Call("BuildSettingsDialog")!;
        var appTab = (TabViewItem)((TabView)settings.Content).TabItems[2];
        var appHeader = (StackPanel)appTab.Header;
        Check(appHeader.Children.OfType<InfoBadge>().Single().Visibility == Visibility.Collapsed, "无新版时应用标签不显示提示点");
        var pendingService = new AppUpdateService(new BadgePreviewBackend(new("0.2.0", "", new object()), pending: true));
        var firstOwner = new Button(); var firstBadge = new InfoBadge();
        var secondOwner = new TabViewItem(); var secondBadge = new InfoBadge();
        UpdateIndicator.Attach(firstOwner, firstBadge, "Settings", pendingService);
        UpdateIndicator.Attach(secondOwner, secondBadge, "SettingsAppTab", pendingService);
        Check(firstBadge.Visibility == Visibility.Visible && secondBadge.Visibility == Visibility.Visible, "待重启更新同步显示设置和应用两个提示点");
        Check(firstBadge.Value == -1 && firstBadge.IconSource is null, "更新提示使用点状原生徽标");
        Check(AutomationProperties.GetName(firstOwner) == "设置，有新版本可用", "设置提示点具有可读的无障碍说明");
        AppText.SetLanguage("en-US");
        Check(AutomationProperties.GetName(secondOwner) == "App, update available", "更新提示及标签无障碍说明即时切换英文");
        AppText.SetLanguage("zh-CN");
        Check((bool)Call("CanPrepareUpdate", true)!, "空闲窗口允许准备更新");
        foreach (string field in new[] { "_pasting", "_deleting", "_pickingFolder", "_closing" })
        {
            Set(field, true);
            Check(!(bool)Call("CanPrepareUpdate", true)!, "文件操作或关闭期间不能准备更新");
            Set(field, false);
        }
        Set("_settingsDialog", new ContentDialog());
        Check(!(bool)Call("CanPrepareUpdate", false)! && (bool)Call("CanPrepareUpdate", true)!, "另一个窗口的对话框阻止重启，发起更新的设置面板允许继续");
        Set("_settingsDialog", null);
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Set("_resumeSaveTask", pending.Task);
        Call("BeginUpdateShutdown");
        Check(!root.IsHitTestVisible && !(bool)Call("CanPrepareUpdate", true)!, "保存期间阻止新的操作");
        var saving = (Task)Call("SaveBeforeUpdateAsync")!;
        Check(!saving.IsCompleted, "重启必须等待续播写入完成");
        pending.SetResult(true);
        await saving;
        Call("CancelUpdateShutdown");
        Check(root.IsHitTestVisible && (bool)Call("CanPrepareUpdate", true)!, "更新失败后窗口恢复交互");
        Set("_resumeSaveTask", Task.FromResult(false));
        bool rejected = false;
        try { await (Task)Call("SaveBeforeUpdateAsync")!; }
        catch (IOException) { rejected = true; }
        Check(rejected, "续播保存失败不得继续更新");
        Set("_resumeSaveTask", Task.FromResult(true));
        return checks;
    }
}
