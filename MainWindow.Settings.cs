using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Fallery;

public sealed partial class MainWindow
{
    private ContentDialog? _settingsDialog;
    private (string Folder, bool PushHistory, bool Forward)? _pendingBrowseNavigation;

    private void UpdateBrowseMenus()
    {
        SortTakenItem.IsChecked = GalleryState.SortField == MediaSortField.Taken;
        SortCreatedItem.IsChecked = GalleryState.SortField == MediaSortField.Created;
        SortModifiedItem.IsChecked = GalleryState.SortField == MediaSortField.Modified;
        SortNameItem.IsChecked = GalleryState.SortField == MediaSortField.Name;
        SortSizeItem.IsChecked = GalleryState.SortField == MediaSortField.Size;
        SortAscendingItem.IsChecked = !GalleryState.Descending;
        SortDescendingItem.IsChecked = GalleryState.Descending;
        FilterAllItem.IsChecked = GalleryState.Filter == MediaFilter.All;
        FilterPhotosItem.IsChecked = GalleryState.Filter == MediaFilter.Photos;
        FilterVideosItem.IsChecked = GalleryState.Filter == MediaFilter.Videos;
        ShowSubfoldersItem.IsChecked = GalleryState.ShowSubfolders;
        HideSubfoldersItem.IsChecked = !GalleryState.ShowSubfolders;
        string label = GalleryState.Filter switch { MediaFilter.Photos => "图片", MediaFilter.Videos => "视频", _ => "所有媒体" };
        ToolTipService.SetToolTip(FilterMenuButton, $"{label} · {(GalleryState.ShowSubfolders ? "显示" : "隐藏")}子文件夹");
    }

    private async void MediaFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string text } && Enum.TryParse<MediaFilter>(text, out var filter))
            await SetFilterAsync(filter, GalleryState.ShowSubfolders);
    }

    private async void Subfolders_Click(object sender, RoutedEventArgs e) =>
        await SetFilterAsync(GalleryState.Filter, (sender as RadioMenuFlyoutItem)?.Tag as string == "Show");

    private async Task SetFilterAsync(MediaFilter filter, bool showSubfolders)
    {
        if (_closed || _closing || !Enum.IsDefined(filter)) return;
        if (GalleryState.Filter == filter && GalleryState.ShowSubfolders == showSubfolders) return;
        GalleryState.Filter = filter;
        GalleryState.ShowSubfolders = showSubfolders;
        UpdateBrowseMenus();
        QueueSettingsSave();
        await ReloadBrowseOptionsAsync();
    }

    private Task ReloadBrowseOptionsAsync()
    {
        // 扫描期间切换选项仍沿用原来的前进/后退动作，不能改成新的历史分支。
        if (GalleryState.IsScanning && _pendingBrowseNavigation is { } request)
            return NavigateCoreAsync(request.Folder, request.PushHistory, refresh: true, request.Forward);
        return GalleryState.CurrentFolder is { } folder ? NavigateAsync(folder, refresh: true) : Task.CompletedTask;
    }

    private ContentDialog BuildSettingsDialog()
    {
        var theme = new ComboBox { Name = "SettingsTheme", Header = "外观", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "跟随系统", "亮色", "暗色" }, SelectedIndex = ThemeSelector.SelectedIndex };
        var mode = new ComboBox { Name = "SettingsView", Header = "默认视图", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "瀑布流", "大图列表", "大图 + 底部缩略图" }, SelectedIndex = (int)GalleryState.DisplayMode };
        var names = new CheckBox { Name = "SettingsNames", Content = "显示文件名", IsChecked = GalleryState.ShowFileNames };
        var size = new Slider { Name = "SettingsSize", Header = "缩略图大小", Minimum = 120, Maximum = 480,
            StepFrequency = 20, Value = ThumbSize.Value };
        var clear = new Button { Content = "清除缩略图缓存" };
        var feedback = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        clear.Click += async (_, _) => { await ClearCacheAsync(); feedback.Text = "已清除，预览会按需重新加载。"; };
        var content = new StackPanel { Spacing = 16, MinWidth = 320 };
        foreach (var element in new UIElement[] { theme, mode, names, size, clear, feedback }) content.Children.Add(element);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = "设置",
            Content = new ScrollViewer { Content = content, MaxHeight = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try { await ApplyPreferencesAsync(theme.SelectedIndex, (GalleryDisplayMode)mode.SelectedIndex, names.IsChecked == true, size.Value); }
            finally { deferral.Complete(); }
        };
        return dialog;
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsDialog is not null || _closed || _closing) return;
        CloseAddressMenu();
        CloseItemMenu();
        var dialog = BuildSettingsDialog();
        _settingsDialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_settingsDialog, dialog)) _settingsDialog = null; }
    }

    private async Task ApplyPreferencesAsync(int theme, GalleryDisplayMode mode, bool showNames, double thumbnailSize)
    {
        if (_closed || _closing) return;
        ThemeSelector.SelectedIndex = Math.Clamp(theme, 0, 2);
        GalleryState.ShowFileNames = showNames;
        ThumbSize.Value = double.IsFinite(thumbnailSize) ? Math.Clamp(thumbnailSize, 120, 480) : 240;
        if (Enum.IsDefined(mode) && mode != GalleryState.DisplayMode) await SetDisplayModeAsync(mode);
        else ApplyDisplayOptions();
        QueueSettingsSave();
    }
}
