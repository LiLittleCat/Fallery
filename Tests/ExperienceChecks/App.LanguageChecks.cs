using System.Reflection;
using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private async Task<int> RunLanguageChecksAsync(MainWindow window, AppSettingsStore store)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Get(MainWindow target, string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(target);
        AppSettings Capture(MainWindow target) => (AppSettings)typeof(MainWindow).GetMethod("CaptureSettings", flags)!.Invoke(target, null)!;
        var root = (FrameworkElement)window.Content;
        var dialog = (ContentDialog)typeof(MainWindow).GetMethod("BuildSettingsDialog", flags)!.Invoke(window, null)!;
        var content = (StackPanel)((ScrollViewer)dialog.Content).Content;
        ComboBox Combo(string name) => content.Children.OfType<ComboBox>().Single(control => control.Name == name);
        var language = Combo("SettingsLanguage");
        var theme = Combo("SettingsTheme");
        var mode = Combo("SettingsView");
        int themeIndex = theme.SelectedIndex, modeIndex = mode.SelectedIndex;
        var items = window.GalleryState.Items;
        var originalMode = window.GalleryState.DisplayMode;
        var tile = new MediaTile();
        tile.Bind(new MediaItem(Path.Combine(Path.GetTempPath(), "设置"), MediaKind.Folder));
        var player = new MouseSeekPlayer { IsMuted = true, Volume = 0, Position = TimeSpan.FromSeconds(23) };
        player.Play();
        typeof(MainWindow).GetField("_videoPlayer", flags)!.SetValue(window, player);
        MainWindow? other = null;
        try
        {
            other = new MainWindow(store, monitorFolderChanges: false);
            typeof(MainWindow).GetField("_initialized", flags)!.SetValue(other, true);
            var otherRoot = (FrameworkElement)other.Content;
            Check(language.SelectedIndex == 0, "Default language selection");
            Check(((RadioMenuFlyoutItem)root.FindName("SortNameItem")).Text == "名称", "Chinese XAML menu");
            var settingsScroll = (ScrollViewer)dialog.Content;
            settingsScroll.Measure(new Windows.Foundation.Size(700, 500));
            double chineseWidth = settingsScroll.DesiredSize.Width;
            var discardedControl = CreateDiscardedLocalizedControl();
            // 子控件只由原生 XAML 树持有时，其托管包装仍可能被 GC 回收。
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Check(!discardedControl.TryGetTarget(out _), "Localization must not retain discarded controls");
            language.SelectedIndex = 1;
            Check(AppText.Language == "en-US", "Setting switches language immediately");
            Check((string)dialog.Title == "Settings" && dialog.CloseButtonText == "Cancel", "Open settings dialog updates");
            Check((string)language.Header == "Language", "Language setting label updates");
            Check(((TextBlock)((StackPanel)content.Children[2]).Children[1]).Text == AppText.Get("LowerValuesAreMoreTransparentIsFullyOpaque"), "Opacity hint updates after GC");
            Check((string)((Slider)content.Children[6]).Header == "Thumbnail size", "Thumbnail size header updates after GC");
            Check(((TextBlock)((StackPanel)content.Children[7]).Children[0]).Text == "Context menu and Open with", "Association heading updates after GC");
            settingsScroll.Measure(new Windows.Foundation.Size(700, 500));
            Check(Math.Abs(settingsScroll.DesiredSize.Width - chineseWidth) < 0.1, "Settings width must not depend on language");
            Check(((TextBlock)tile.FindName("ListType")).Text == "Folder", "Existing list item type updates");
            Check(((TextBlock)tile.FindName("ListName")).Text == "设置", "Existing folder name stays unchanged");
            var details = MediaDetailsReader.Basic(Path.Combine(Path.GetTempPath(), "设置.jpg"), MediaKind.Image);
            Check(details.Groups.SelectMany(group => group.Rows).Any(row => row.Label == "File name" && row.Value == "设置.jpg"), "English metadata labels preserve file names");
            Check((string)theme.Items[0] == "Use system setting", "Settings option text updates");
            Check(theme.SelectedIndex == themeIndex && mode.SelectedIndex == modeIndex, "Translated option lists preserve selection");
            Check(window.GalleryState.DisplayMode == originalMode && ReferenceEquals(items, window.GalleryState.Items), "Language does not rebuild gallery or change view");
            Check(ReferenceEquals(Get(window, "_videoPlayer"), player) && player.IsPlaying && player.Position.TotalSeconds == 23 && player.IsMuted && player.Volume == 0,
                "Language must not interrupt playback or alter its position/volume");
            Check(((RadioMenuFlyoutItem)root.FindName("SortNameItem")).Text == "Name", "Main menu updates");
            Check(((RadioMenuFlyoutItem)otherRoot.FindName("SortNameItem")).Text == "Name", "Existing second window updates");
            Check(AutomationProperties.GetName((DependencyObject)root.FindName("SidebarToggle")) == "Show or hide folder tree", "Accessibility label updates");
            Check(Capture(window).Language == "en-US" && Capture(other).Language == "en-US", "All windows save the shared language");
            Check(await store.SaveAsync(Capture(window)) && (await store.LoadAsync()).Language == "en-US", "Language persists in isolated settings");
            var fileLabel = new TextBlock();
            LiveText.Set(fileLabel, "Text", control => control.Text = AppText.Get("Settings"));
            LiveText.SetOnce(fileLabel, "Text", control => control.Text = "设置.jpg");
            language.SelectedIndex = 0;
            Check(((TextBlock)tile.FindName("ListType")).Text == "文件夹", "List item type switches back");
            tile.Bind(null);
            AppText.SetLanguage("en-US");
            Check(((TextBlock)tile.FindName("ListName")).Text == "" && ((TextBlock)tile.FindName("ListType")).Text == "", "Recycled tile remains clear during language changes");
            AppText.SetLanguage("zh-CN");
            Check(fileLabel.Text == "设置.jpg", "Reused controls must not translate filenames");
            Check(((RadioMenuFlyoutItem)root.FindName("SortNameItem")).Text == "名称" && (string)dialog.Title == "设置", "Switching back restores Chinese");
            Check(theme.SelectedIndex == themeIndex && mode.SelectedIndex == modeIndex, "Round-trip preserves other settings");
            Check((new AppSettings { Language = "unsupported" }).Normalize().Language == "zh-CN", "Invalid saved language falls back");
        }
        finally
        {
            AppText.SetLanguage("zh-CN");
            typeof(MainWindow).GetField("_videoPlayer", flags)!.SetValue(window, null);
            player.Pause();
            if (other is not null)
            {
                typeof(MainWindow).GetField("_allowClose", flags)!.SetValue(other, true);
                other.Close();
            }
        }
        return checks;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference<TextBlock> CreateDiscardedLocalizedControl()
    {
        var control = LiveText.Set(new TextBlock(), "Text", static target => target.Text = AppText.Get("Settings"));
        return new(control);
    }
}
