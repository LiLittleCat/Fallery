using System.Reflection;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.Controls;

public sealed class Localization : DependencyObject
{
    public static readonly DependencyProperty KeysProperty = DependencyProperty.RegisterAttached(
        "Keys", typeof(string), typeof(Localization), new PropertyMetadata("", OnKeysChanged));
    public static string GetKeys(DependencyObject target) => (string)target.GetValue(KeysProperty);
    public static void SetKeys(DependencyObject target, string value) => target.SetValue(KeysProperty, value);

    private static void OnKeysChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        foreach (string entry in ((string)args.NewValue).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = entry.Split('=', 2);
            string property = pair[0], key = pair[1];
            if (property == "ToolTipService.ToolTip")
                LiveText.Set(target, property, control => ToolTipService.SetToolTip(control, AppText.Get(key)));
            else if (property == "AutomationProperties.Name")
                LiveText.Set(target, property, control => AutomationProperties.SetName(control, AppText.Get(key)));
            else
            {
                var info = target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new InvalidOperationException($"Unknown localized property: {property}");
                LiveText.Set(target, property, control => info.SetValue(control, AppText.Get(key)));
            }
        }
    }
}
