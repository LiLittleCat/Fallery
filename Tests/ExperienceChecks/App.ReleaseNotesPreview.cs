using Fallery.Controls;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task ShowReleaseNotesPreviewAsync(string workspace)
    {
        // 人工预览使用内存更新源；不创建播放器、读取用户配置或触发真实更新。
        string sample = await File.ReadAllTextAsync(Path.Combine(workspace, "work", "validation", "release-notes-preview.md"));
        AppText.SetLanguage("zh-CN");
        var window = new Window { Title = AppText.Get("ReleaseNotesPreviewTitle") };
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(24), MaxWidth = 480 };
        var language = new ComboBox { ItemsSource = new[] { "简体中文", "English" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        LiveText.Set(language, "Header", control => control.Header = AppText.Get("Language"));
        language.SelectionChanged += (_, _) =>
        {
            AppText.SetLanguage(language.SelectedIndex == 1 ? "en-US" : "zh-CN");
            window.Title = AppText.Get("ReleaseNotesPreviewTitle");
        };
        panel.Children.Add(language);
        var service = new AppUpdateService(new BadgePreviewBackend(new("0.1.2", sample, new object())));
        var settingsContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        settingsContent.Children.Add(new SymbolIcon(Symbol.Setting));
        settingsContent.Children.Add(LiveText.Set(new TextBlock(), "Text", text => text.Text = AppText.Get("Settings")));
        var settingsBadge = new InfoBadge { Width = 8, Height = 8, MinWidth = 8, MinHeight = 8, VerticalAlignment = VerticalAlignment.Center };
        settingsContent.Children.Add(settingsBadge);
        var settingsButton = new Button { Content = settingsContent };
        UpdateIndicator.Attach(settingsButton, settingsBadge, "Settings", service);
        panel.Children.Add(settingsButton);
        var tabs = new TabView { IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false };
        var appHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        appHeader.Children.Add(LiveText.Set(new TextBlock(), "Text", text => text.Text = AppText.Get("SettingsAppTab")));
        var appBadge = new InfoBadge { Width = 8, Height = 8, MinWidth = 8, MinHeight = 8, VerticalAlignment = VerticalAlignment.Center };
        appHeader.Children.Add(appBadge);
        var appTab = new TabViewItem { Header = appHeader, IsClosable = false };
        UpdateIndicator.Attach(appTab, appBadge, "SettingsAppTab", service);
        tabs.TabItems.Add(LiveText.Set(new TabViewItem { IsClosable = false }, "Header", tab => tab.Header = AppText.Get("SettingsGeneralTab")));
        tabs.TabItems.Add(appTab);
        settingsButton.Click += (_, _) => tabs.SelectedItem = appTab;
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(0, 12, 0, 0) };
        content.Children.Add(LiveText.Set(new TextBlock { FontSize = 18 }, "Text", control => control.Text = AppText.Get("VersionAndUpdates")));
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        LiveText.Set(status, "Text", control => control.Text = service.Status.DisplayMessage);
        content.Children.Add(status);
        var notes = new ReleaseNotesView();
        content.Children.Add(notes);
        service.Changed += () => { status.Text = service.Status.DisplayMessage; notes.SetNotes(service.Status.Release); };
        appTab.Content = content;
        panel.Children.Add(tabs);
        var checker = new AutomaticUpdateChecker(service);
        window.Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = (Brush)Current.Resources["ApplicationPageBackgroundThemeBrush"] };
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(550, 620));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => { checker.Stop(); closed.TrySetResult(); };
        window.Activate();
        checker.Start();
        await closed.Task;
        await checker.Completion;
    }

    private sealed class BadgePreviewBackend(AppUpdateRelease release, bool pending = false) : IAppUpdateBackend
    {
        public AppUpdateRelease? Pending => pending ? release : null;
        public Task<AppUpdateRelease?> CheckAsync(CancellationToken ct) => Task.FromResult<AppUpdateRelease?>(release);
        public Task DownloadAsync(AppUpdateRelease release, Action<int> progress, CancellationToken ct) => throw new InvalidOperationException("Preview cannot download updates.");
        public void ScheduleRestart(AppUpdateRelease release) => throw new InvalidOperationException("Preview cannot install updates.");
    }
}
