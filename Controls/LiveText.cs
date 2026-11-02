using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.Controls;

/// <summary>只刷新显式登记的文案，不重建窗口、媒体控件或文件列表。</summary>
public static class LiveText
{
    // 原生 XAML 树可以继续持有控件，而托管包装已被 GC 回收；不能只用包装作为弱表键。
    // 把回调保存在控件自己的依赖属性中，让它与原生控件共同存活。
    private static readonly DependencyProperty SubscriptionProperty = DependencyProperty.RegisterAttached(
        "TextSubscription", typeof(object), typeof(Localization), new PropertyMetadata(null));
    private sealed class Subscription(DependencyObject target)
    {
        public DependencyObject Target { get; } = target;
        public Dictionary<string, Action> Callbacks { get; } = [];
    }
    private static readonly List<WeakReference<Subscription>> Targets = [];

    static LiveText() => AppText.Changed += _ => Refresh();

    public static T Set<T>(T target, string property, Action<T> update) where T : DependencyObject
    {
        if (target.GetValue(SubscriptionProperty) is not Subscription subscription)
        {
            subscription = new(target);
            target.SetValue(SubscriptionProperty, subscription);
            Targets.Add(new(subscription));
            if (Targets.Count % 256 == 0) Targets.RemoveAll(reference => !reference.TryGetTarget(out _));
        }
        subscription.Callbacks[property] = () => update(target);
        int selected = target is ComboBox combo && property == "ItemsSource" ? combo.SelectedIndex : -1;
        update(target);
        if (selected >= 0 && target is ComboBox selector) selector.SelectedIndex = selected;
        return target;
    }

    public static T SetOnce<T>(T target, string property, Action<T> update) where T : DependencyObject
    {
        if (target.GetValue(SubscriptionProperty) is Subscription subscription) subscription.Callbacks.Remove(property);
        update(target);
        return target;
    }

    private static void Refresh()
    {
        Targets.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in Targets.ToArray())
        {
            if (!reference.TryGetTarget(out var subscription)) continue;
            var target = subscription.Target;
            if (!target.DispatcherQueue.HasThreadAccess)
                target.DispatcherQueue.TryEnqueue(() => Apply(target, subscription.Callbacks));
            else Apply(target, subscription.Callbacks);
        }
    }

    private static void Apply(object target, Dictionary<string, Action> callbacks)
    {
        foreach (var (property, update) in callbacks.ToArray())
        {
            // 替换选项文案时保留枚举索引，避免语言变化变成一次设置操作。
            int selected = target is ComboBox combo && property == "ItemsSource" ? combo.SelectedIndex : -1;
            update();
            if (selected >= 0 && target is ComboBox selector) selector.SelectedIndex = selected;
        }
    }
}
