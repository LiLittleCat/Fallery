using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Fallery;

public sealed partial class MainWindow
{
    private ContentDialog? _settingsDialog;
    private double _backgroundOpacity = 70;

    private void ApplyBackgroundOpacity(double value)
    {
        _backgroundOpacity = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 70;
        // 只覆盖系统材质，不改变文字、控件或媒体自身的透明度。
        BackdropTint.Opacity = _backgroundOpacity / 100;
        QueueSettingsSave();
    }
    private (string Folder, bool PushHistory, bool Forward)? _pendingBrowseNavigation;

    private void UpdateBrowseMenus()
    {
        UpdateDetailsHeaders();
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
        FilterAudioItem.IsChecked = GalleryState.Filter == MediaFilter.Audio;
        ShowSubfoldersItem.IsChecked = GalleryState.ShowSubfolders;
        HideSubfoldersItem.IsChecked = !GalleryState.ShowSubfolders;
        ShowHiddenItem.IsChecked = GalleryState.ShowHidden;
        string label = GalleryState.Filter switch { MediaFilter.Photos => AppText.Get("Photos"), MediaFilter.Videos => AppText.Get("Videos"), MediaFilter.Audio => AppText.Get("Audio"), _ => AppText.Get("AllMedia") };
        LiveText.Set(FilterMenuButton, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, AppText.Format("Subfolders805d87", label, (GalleryState.ShowSubfolders ? AppText.Get("Show") : AppText.Get("Hide")))
            + (GalleryState.ShowHidden ? AppText.Get("IncludingHiddenItems") : "")));
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
        [GalleryDisplayMode.Justified, GalleryDisplayMode.Masonry, GalleryDisplayMode.LargeIcons, GalleryDisplayMode.Filmstrip, GalleryDisplayMode.Details];

    private ContentDialog BuildSettingsDialog()
    {
        var languagePreview = new LanguagePreview();
        var language = new ComboBox
        {
            Name = "SettingsLanguage", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "简体中文", "English" }, SelectedIndex = AppText.Language == "en-US" ? 1 : 0,
        };
        LiveText.Set(language, "Header", control => control.Header = AppText.Get("Language"));
        LiveText.Set(language, "SelectedIndex", control => control.SelectedIndex = AppText.Language == "en-US" ? 1 : 0);
        var theme = LiveText.Set(LiveText.Set(new ComboBox { Name = "SettingsTheme", Header = AppText.Get("Appearance"), HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { AppText.Get("UseSystemSetting"), AppText.Get("Light"), AppText.Get("Dark") }, SelectedIndex = _themeIndex }, "Header", localized => localized.Header = AppText.Get("Appearance")), "ItemsSource", localized => localized.ItemsSource = new[] { AppText.Get("UseSystemSetting"), AppText.Get("Light"), AppText.Get("Dark") });
        var mode = LiveText.Set(LiveText.Set(new ComboBox { Name = "SettingsView", Header = AppText.Get("DefaultView"), HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { AppText.Get("JustifiedView"), AppText.Get("MasonryView"), AppText.Get("GridView"), AppText.Get("LargeImageFilmstrip"), AppText.Get("DetailsView") },
            SelectedIndex = Math.Max(0, Array.IndexOf(DisplayModeOrder, GalleryState.DisplayMode)) }, "Header", localized => localized.Header = AppText.Get("DefaultView")), "ItemsSource", localized => localized.ItemsSource = new[] { AppText.Get("JustifiedView"), AppText.Get("MasonryView"), AppText.Get("GridView"), AppText.Get("LargeImageFilmstrip"), AppText.Get("DetailsView") });
        var opacity = LiveText.Set(new Slider { Name = "SettingsBackgroundOpacity", Header = AppText.Format("BackgroundOpacity", _backgroundOpacity),
            Minimum = 0, Maximum = 100, StepFrequency = 1, Value = _backgroundOpacity }, "Header", localized => localized.Header = AppText.Format("BackgroundOpacity", _backgroundOpacity));
        var opacityHint = LiveText.Set(new TextBlock { Text = AppText.Get("LowerValuesAreMoreTransparentIsFullyOpaque"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] }, "Text", localized => localized.Text = AppText.Get("LowerValuesAreMoreTransparentIsFullyOpaque"));
        var opacitySection = new StackPanel { Spacing = 4 };
        opacitySection.Children.Add(opacity);
        opacitySection.Children.Add(opacityHint);
        var names = LiveText.Set(new CheckBox { Name = "SettingsNames", Content = AppText.Get("ShowFileNames"), IsChecked = GalleryState.ShowFileNames }, "Content", localized => localized.Content = AppText.Get("ShowFileNames"));
        var cards = LiveText.Set(new CheckBox { Name = "SettingsCards", Content = AppText.Get("ShowCardBackgrounds"), IsChecked = GalleryState.ShowCards }, "Content", localized => localized.Content = AppText.Get("ShowCardBackgrounds"));
        var size = LiveText.Set(new Slider { Name = "SettingsSize", Header = AppText.Get("ThumbnailSize"), Minimum = 120, Maximum = 480,
            StepFrequency = 20, Value = ThumbSize.Value }, "Header", localized => localized.Header = AppText.Get("ThumbnailSize"));
        var association = BuildAssociationSection();
        var clear = LiveText.Set(new Button { Content = AppText.Get("ClearThumbnailCache") }, "Content", localized => localized.Content = AppText.Get("ClearThumbnailCache"));
        var feedback = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        clear.Click += async (_, _) => { await ClearCacheAsync(); LiveText.SetOnce(feedback, "Text", localized => localized.Text = GalleryState.Status); };
        // WinUI 的滚动条覆盖在内容之上，给它单独留出右侧空间。
        var content = new StackPanel { Spacing = 16, Margin = new Thickness(0, 0, 24, 4) };
        foreach (var element in new UIElement[] { language, theme, opacitySection, mode, names, cards, size, association, clear, feedback, BuildUpdateSection() }) content.Children.Add(element);
        double ContentWidth() => Root.ActualWidth > 0 ? Math.Clamp(Root.ActualWidth - 112, 0, 400) : 400;
        var scroll = new ScrollViewer
        {
            Content = content, Width = ContentWidth(), MaxHeight = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled,
        };
        var dialog = LiveText.Set(LiveText.Set(LiveText.Set(new ContentDialog
        {
            XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = AppText.Get("Settings"),
            Content = scroll,
            PrimaryButtonText = AppText.Get("Done"), CloseButtonText = AppText.Get("Cancel"), DefaultButton = ContentDialogButton.Primary,
        }, "Title", localized => localized.Title = AppText.Get("Settings")), "PrimaryButtonText", localized => localized.PrimaryButtonText = AppText.Get("Done")), "CloseButtonText", localized => localized.CloseButtonText = AppText.Get("Cancel"));
        // 固定内容宽度，避免语言和长说明改变弹窗尺寸；小窗口仍按可用宽度收窄。
        SizeChangedEventHandler resize = (_, _) => scroll.Width = ContentWidth();
        dialog.Opened += (_, _) => { scroll.Width = ContentWidth(); Root.SizeChanged += resize; };
        dialog.Closed += (_, _) => Root.SizeChanged -= resize;
        // 每项改动立即应用以便预览；取消时恢复打开面板前的状态。
        var original = (Theme: _themeIndex, Opacity: _backgroundOpacity, Mode: GalleryState.DisplayMode, Names: GalleryState.ShowFileNames, Cards: GalleryState.ShowCards, Size: ThumbSize.Value);
        language.SelectionChanged += (_, _) =>
        {
            if (AppText.IsChanging || language.SelectedIndex < 0) return;
            languagePreview.Select(language.SelectedIndex == 1 ? "en-US" : "zh-CN");
        };
        dialog.Closing += (_, args) =>
        {
            // 其他窗口较新的语言选择优先，避免旧设置面板取消时覆盖它。
            languagePreview.Finish(args.Result == ContentDialogResult.Primary);
            QueueSettingsSave();
        };
        opacity.ValueChanged += (_, args) =>
        {
            ApplyBackgroundOpacity(args.NewValue);
            LiveText.Set(opacity, "Header", localized => localized.Header = AppText.Format("BackgroundOpacity", _backgroundOpacity));
        };
        // 只应用改动的那一项：缩略图大小走原有的布局刷新，不重建全部卡片。
        theme.SelectionChanged += (_, _) =>
        {
            if (AppText.IsChanging) return;
            ApplyTheme(theme.SelectedIndex);
            dialog.RequestedTheme = Root.RequestedTheme;
        };
        mode.SelectionChanged += async (_, _) =>
        {
            if (AppText.IsChanging) return;
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
                ApplyBackgroundOpacity(original.Opacity);
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
        panel.Children.Add(LiveText.Set(new TextBlock { Text = AppText.Get("ContextMenuAndOpenWith"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] }, "Text", localized => localized.Text = AppText.Get("ContextMenuAndOpenWith")));
        if (AppIdentity.IsDevelopment)
        {
            panel.Children.Add(LiveText.Set(new TextBlock { Text = AppText.Get("TheDevelopmentBuildUsesSeparateDataAndDoesNot"), TextWrapping = TextWrapping.Wrap }, "Text", localized => localized.Text = AppText.Get("TheDevelopmentBuildUsesSeparateDataAndDoesNot")));
            return panel;
        }
        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var register = LiveText.Set(new Button { Content = AppText.Get("RegisterContextMenuAndFileAssociations") }, "Content", localized => localized.Content = AppText.Get("RegisterContextMenuAndFileAssociations"));
        var unregister = LiveText.Set(new Button { Content = AppText.Get("Unregister") }, "Content", localized => localized.Content = AppText.Get("Unregister"));
        string exe = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Fallery.exe");
        void Refresh()
        {
            var registered = FileAssociation.RegisteredExecutable();
            bool current = registered is not null && string.Equals(System.IO.Path.GetFullPath(registered), exe, StringComparison.OrdinalIgnoreCase);
            LiveText.Set(status, "Text", localized => localized.Text = registered is null ? AppText.Get("AfterRegistrationRightClickAPhotoVideoAudioFile")
                : current ? FileAssociation.IsContextMenuRegistered(exe, FolderScanner.SupportedExtensions)
                    ? AppText.Get("ContextMenuAndFileAssociationsRegisteredOnWindowsLook")
                    : AppText.Get("FileAssociationsRegisteredRegisterAgainToAddMissingFile")
                : AppText.Format("RegisteredAtAnotherLocationRegisterAgainAfterMovingThe", registered));
            unregister.IsEnabled = current;
        }
        register.Click += (_, _) =>
        {
            try { FileAssociation.Register(exe, FolderScanner.SupportedExtensions); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { LiveText.Set(status, "Text", localized => localized.Text = AppText.Format("RegistrationFailed", ex.Message)); return; }
            Refresh();
        };
        unregister.Click += (_, _) =>
        {
            try { FileAssociation.Unregister(FolderScanner.SupportedExtensions, expectedExePath: exe); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { LiveText.Set(status, "Text", localized => localized.Text = AppText.Format("UnregistrationFailed", ex.Message)); return; }
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
