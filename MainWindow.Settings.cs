using Fallery.Models;
using Fallery.Services;
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
        ShowHiddenItem.IsChecked = GalleryState.ShowHidden;
        string label = GalleryState.Filter switch { MediaFilter.Photos => "图片", MediaFilter.Videos => "视频", _ => "所有媒体" };
        ToolTipService.SetToolTip(FilterMenuButton, $"{label} · {(GalleryState.ShowSubfolders ? "显示" : "隐藏")}子文件夹"
            + (GalleryState.ShowHidden ? " · 含隐藏项目" : ""));
    }

    private async void MediaFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string text } && Enum.TryParse<MediaFilter>(text, out var filter))
            await SetFilterAsync(filter, GalleryState.ShowSubfolders);
    }

    private async void Subfolders_Click(object sender, RoutedEventArgs e) =>
        await SetFilterAsync(GalleryState.Filter, (sender as RadioMenuFlyoutItem)?.Tag as string == "Show");

    private async void ShowHidden_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _closing) return;
        bool showHidden = ShowHiddenItem.IsChecked;
        if (GalleryState.ShowHidden == showHidden) return;
        GalleryState.ShowHidden = showHidden;
        UpdateBrowseMenus();
        QueueSettingsSave();
        await ResetFolderTreeAsync();
        await ReloadBrowseOptionsAsync();
    }

    private async Task ResetFolderTreeAsync()
    {
        // 已展开的节点按旧选项加载，全部收回为未加载；随后的刷新导航会按新选项重新定位当前目录。
        foreach (var pending in _expandingNodes.Values.ToList())
        {
            try { await pending; }
            catch (OperationCanceledException) { }
        }
        if (_closed) return;
        foreach (var root in FolderTree.RootNodes)
        {
            root.IsExpanded = false;
            root.Children.Clear();
            root.HasUnrealizedChildren = true;
        }
    }

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

    // 设置面板的显示顺序与枚举数值（持久化用）不同。
    private static readonly GalleryDisplayMode[] DisplayModeOrder =
        [GalleryDisplayMode.Justified, GalleryDisplayMode.Masonry, GalleryDisplayMode.LargeIcons, GalleryDisplayMode.Filmstrip];

    private ContentDialog BuildSettingsDialog()
    {
        var theme = new ComboBox { Name = "SettingsTheme", Header = "外观", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "跟随系统", "亮色", "暗色" }, SelectedIndex = _themeIndex };
        var mode = new ComboBox { Name = "SettingsView", Header = "默认视图", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "自适应视图", "瀑布视图", "网格视图", "大图 + 底部缩略图" },
            SelectedIndex = Math.Max(0, Array.IndexOf(DisplayModeOrder, GalleryState.DisplayMode)) };
        var names = new CheckBox { Name = "SettingsNames", Content = "显示文件名", IsChecked = GalleryState.ShowFileNames };
        var cards = new CheckBox { Name = "SettingsCards", Content = "显示卡片背景", IsChecked = GalleryState.ShowCards };
        var size = new Slider { Name = "SettingsSize", Header = "缩略图大小", Minimum = 120, Maximum = 480,
            StepFrequency = 20, Value = ThumbSize.Value };
        var association = BuildAssociationSection();
        var clear = new Button { Content = "清除缩略图缓存" };
        var feedback = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        clear.Click += async (_, _) => { await ClearCacheAsync(); feedback.Text = GalleryState.Status; };
        var content = new StackPanel { Spacing = 16, MinWidth = 320 };
        foreach (var element in new UIElement[] { theme, mode, names, cards, size, association, clear, feedback }) content.Children.Add(element);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = "设置",
            Content = new ScrollViewer { Content = content, MaxHeight = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "完成", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary,
        };
        // 每项改动立即应用以便预览；取消时恢复打开面板前的状态。
        var original = (Theme: _themeIndex, Mode: GalleryState.DisplayMode, Names: GalleryState.ShowFileNames, Cards: GalleryState.ShowCards, Size: ThumbSize.Value);
        // 只应用改动的那一项：缩略图大小走原有的布局刷新，不重建全部卡片。
        theme.SelectionChanged += (_, _) =>
        {
            ApplyTheme(theme.SelectedIndex);
            dialog.RequestedTheme = Root.RequestedTheme;
        };
        mode.SelectionChanged += async (_, _) =>
        {
            var selected = DisplayModeOrder[Math.Clamp(mode.SelectedIndex, 0, DisplayModeOrder.Length - 1)];
            if (selected != GalleryState.DisplayMode) await SetDisplayModeAsync(selected);
        };
        names.Click += (_, _) =>
        {
            GalleryState.ShowFileNames = names.IsChecked == true;
            ApplyDisplayOptions();
            QueueSettingsSave();
        };
        cards.Click += (_, _) =>
        {
            GalleryState.ShowCards = cards.IsChecked == true;
            ApplyDisplayOptions();
            QueueSettingsSave();
        };
        size.ValueChanged += (_, args) => ThumbSize.Value = args.NewValue;
        dialog.CloseButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                GalleryState.ShowCards = original.Cards;
                await ApplyPreferencesAsync(original.Theme, original.Mode, original.Names, original.Size);
            }
            finally { deferral.Complete(); }
        };
        return dialog;
    }

    /// <summary>写入当前用户的“打开方式”列表；默认应用仍需用户在系统里选择。</summary>
    private StackPanel BuildAssociationSection()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "打开方式", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var register = new Button { Content = "注册到“打开方式”" };
        var unregister = new Button { Content = "取消注册" };
        string exe = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Fallery.exe");
        void Refresh()
        {
            var registered = FileAssociation.RegisteredExecutable();
            bool current = registered is not null && string.Equals(System.IO.Path.GetFullPath(registered), exe, StringComparison.OrdinalIgnoreCase);
            status.Text = registered is null ? "尚未注册。注册后可在资源管理器右键“打开方式”中选择 Fallery。"
                : current ? "已注册。可在“打开方式”或系统“默认应用”中把 Fallery 设为默认。"
                : $"已注册到其他位置：{registered}\n程序移动后请重新注册。";
            unregister.IsEnabled = registered is not null;
        }
        register.Click += (_, _) =>
        {
            try { FileAssociation.Register(exe, FolderScanner.SupportedExtensions); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { status.Text = $"注册失败：{ex.Message}"; return; }
            Refresh();
        };
        unregister.Click += (_, _) =>
        {
            try { FileAssociation.Unregister(FolderScanner.SupportedExtensions); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { status.Text = $"取消注册失败：{ex.Message}"; return; }
            Refresh();
        };
        Refresh();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(register);
        buttons.Children.Add(unregister);
        panel.Children.Add(status);
        panel.Children.Add(buttons);
        return panel;
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsDialog is not null || _closed || _closing) return;
        StopHoverPreview();
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
        ApplyTheme(theme);
        GalleryState.ShowFileNames = showNames;
        ThumbSize.Value = double.IsFinite(thumbnailSize) ? Math.Clamp(thumbnailSize, 120, 480) : 240;
        if (Enum.IsDefined(mode) && mode != GalleryState.DisplayMode) await SetDisplayModeAsync(mode);
        else ApplyDisplayOptions();
        QueueSettingsSave();
    }
}
