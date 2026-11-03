using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.Controls;

/// <summary>入口和标签复用同一状态，卸载后解除订阅，避免共享服务持有已关闭窗口。</summary>
public static class UpdateIndicator
{
    public static void Attach(FrameworkElement owner, InfoBadge badge, string labelKey, AppUpdateService service)
    {
        bool listening = false;
        void Refresh()
        {
            bool available = service.Status.HasUpdate;
            badge.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            string label = AppText.Get(labelKey);
            string description = available ? AppText.Format("UpdateAvailableAt", label) : label;
            AutomationProperties.SetName(owner, description);
            ToolTipService.SetToolTip(owner, description);
        }
        void Changed()
        {
            if (owner.DispatcherQueue.HasThreadAccess) Refresh();
            else owner.DispatcherQueue.TryEnqueue(() => { if (listening) Refresh(); });
        }
        void Subscribe()
        {
            if (listening) return;
            listening = true;
            service.Changed += Changed;
            Refresh();
        }
        owner.Loaded += (_, _) => Subscribe();
        owner.Unloaded += (_, _) => { service.Changed -= Changed; listening = false; };
        LiveText.Set(owner, "UpdateIndicator", _ => Refresh());
        if (owner.IsLoaded) Subscribe();
    }
}
