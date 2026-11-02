using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace Fallery;

public sealed partial class MainWindow
{
    public sealed record IdenticalImageRow(IdenticalImageMatch Match)
    {
        public string Name => $"{Match.Name}  ·  {(Match.Mode == IdenticalImageMatchMode.NameOnly ? AppText.Get("SameNameOnly") : AppText.Get("SameNameSizeAndContent"))}";
        public string Parent => Match.Parent;
        public string Path => Match.Path;
        public MediaFile PreviewFile { get; } = new(Match.Path, MediaKind.Image) { Version = Match.Version };
    }

    private bool CanFindIdenticalImages(IReadOnlyList<(string Path, bool Folder)> targets) =>
        targets.Count is > 0 and <= IdenticalImages.SampleLimit
        && targets.All(target => !target.Folder && FolderScanner.GetMediaKind(target.Path) == MediaKind.Image)
        && GalleryState.CurrentFolder is not null && !ViewerState.IsOpen;

    private async Task FindIdenticalImagesAsync(IReadOnlyList<(string Path, bool Folder)> targets)
    {
        if (!CanFindIdenticalImages(targets) || _settingsDialog is not null || _searchDialog is not null
            || _deleting || _pasting || _closed || _closing || GalleryState.CurrentFolder is not { } folder) return;
        if (!SupportsRecycleBin(folder))
        { GalleryState.Status = AppText.Get("IdenticalImageCleanupRequiresALocalFixedDriveThat"); return; }
        StopHoverPreview(); CloseItemMenu(); CloseAddressMenu();
        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        bool active = true;
        var summary = LiveText.Set(new TextBlock { Text = AppText.Get("ChooseASearchLocationThenStartSearching"), TextWrapping = TextWrapping.Wrap }, "Text", localized => localized.Text = AppText.Get("ChooseASearchLocationThenStartSearching"));
        var selectedCount = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
        var ancestors = new List<string>();
        for (string? parent = folder; parent is not null; parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(parent))) ancestors.Add(parent);
        var scope = CreateIdenticalImageScope(ancestors);
        var nameOnly = CreateIdenticalImageMatchOption();
        var matchMode = IdenticalImageMatchMode.NameSizeContent;
        var start = LiveText.Set(new Button { Content = AppText.Get("StartSearch") }, "Content", localized => localized.Content = AppText.Get("StartSearch"));
        var preview = new Image { Width = 96, Height = 80, Stretch = Stretch.Uniform };
        var previewName = LiveText.Set(new TextBlock { Text = AppText.Get("SelectAResultToPreviewTheImage"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3, MaxWidth = 430 }, "Text", localized => localized.Text = AppText.Get("SelectAResultToPreviewTheImage"));
        var previewPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        previewPanel.Children.Add(preview); previewPanel.Children.Add(previewName);
        var results = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            ItemTemplate = (DataTemplate)Root.Resources["IdenticalImageResultTemplate"]
        };
        var all = LiveText.Set(new Button { Content = AppText.Get("SelectAll"), IsEnabled = false }, "Content", localized => localized.Content = AppText.Get("SelectAll"));
        var none = LiveText.Set(new Button { Content = AppText.Get("DeselectAll"), IsEnabled = false }, "Content", localized => localized.Content = AppText.Get("DeselectAll"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(all); actions.Children.Add(none);
        all.Click += (_, _) => results.SelectAll(); none.Click += (_, _) => results.SelectedItems.Clear();
        var scopeRow = new Grid { ColumnSpacing = 8 };
        scopeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scopeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        scopeRow.Children.Add(scope); scopeRow.Children.Add(start); Grid.SetColumn(start, 1);
        start.VerticalAlignment = VerticalAlignment.Bottom;
        var description = LiveText.Set(new TextBlock { Text = AppText.Get("MatchesNameSizeAndContentByDefaultIncludesSubfolders"), TextWrapping = TextWrapping.Wrap, FontSize = 12 }, "Text", localized => localized.Text = AppText.Get("MatchesNameSizeAndContentByDefaultIncludesSubfolders"));
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(scopeRow); header.Children.Add(nameOnly); header.Children.Add(description);
        header.Children.Add(progress); header.Children.Add(summary); header.Children.Add(previewPanel); header.Children.Add(actions);
        var content = CreateIdenticalImagesContent(header, results, selectedCount);
        void UpdateLayoutSize()
        {
            SizeIdenticalImagesContent(content, Root.ActualWidth, Root.ActualHeight);
            previewName.MaxWidth = Math.Max(0, content.MaxWidth - 120);
        }
        void UpdateCompactHeader(object sender, SizeChangedEventArgs args)
        {
            previewPanel.Visibility = args.NewSize.Height >= 420 ? Visibility.Visible : Visibility.Collapsed;
            description.Visibility = args.NewSize.Height >= 340 ? Visibility.Visible : Visibility.Collapsed;
        }
        content.SizeChanged += UpdateCompactHeader;
        UpdateLayoutSize();
        void OnRootSizeChanged(object sender, SizeChangedEventArgs args) => UpdateLayoutSize();
        var dialog = LiveText.Set(LiveText.Set(LiveText.Set(LiveText.Set(new ContentDialog
        {
            XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = AppText.Get("FindAndRemoveIdenticalImages"),
            Content = content,
            PrimaryButtonText = AppText.Get("MoveToRecycleBin"), IsPrimaryButtonEnabled = false,
            SecondaryButtonText = AppText.Get("DeletePermanently"), IsSecondaryButtonEnabled = false,
            CloseButtonText = AppText.Get("Cancel"), DefaultButton = ContentDialogButton.Close
        }, "Title", localized => localized.Title = AppText.Get("FindAndRemoveIdenticalImages")), "PrimaryButtonText", localized => localized.PrimaryButtonText = AppText.Get("MoveToRecycleBin")), "SecondaryButtonText", localized => localized.SecondaryButtonText = AppText.Get("DeletePermanently")), "CloseButtonText", localized => localized.CloseButtonText = AppText.Get("Cancel"));
        dialog.Resources["ContentDialogMaxWidth"] = 760d;
        int previewVersion = 0;
        int scanVersion = 0;
        bool searching = false, hasSearched = false;
        var previewSlot = new SemaphoreSlim(1);
        async Task PreviewAsync(IdenticalImageMatch match)
        {
            int version = ++previewVersion;
            preview.Source = null; LiveText.SetOnce(previewName, "Text", localized => localized.Text = match.Path);
            await previewSlot.WaitAsync();
            try
            {
                if (!active || version != previewVersion) return;
                var file = await StorageFile.GetFileFromPathAsync(match.Path);
                using var stream = await file.OpenReadAsync();
                var bitmap = new BitmapImage { DecodePixelWidth = 96, DecodePixelHeight = 96 };
                await bitmap.SetSourceAsync(stream);
                if (active && version == previewVersion) preview.Source = bitmap;
            }
            catch (Exception) { if (active && version == previewVersion) LiveText.Set(previewName, "Text", localized => localized.Text = AppText.Format("PreviewUnavailable", match.Path)); }
            finally { previewSlot.Release(); }
        }
        results.SelectionChanged += (_, args) =>
        {
            dialog.IsPrimaryButtonEnabled = results.SelectedItems.Count > 0;
            dialog.IsSecondaryButtonEnabled = results.SelectedItems.Count > 0;
            LiveText.Set(selectedCount, "Text", localized => localized.Text = AppText.Format("ItemsSelectedIncludingCheckedSampleFiles", results.SelectedItems.Count));
            if (args.AddedItems.OfType<IdenticalImageRow>().LastOrDefault() is { } row) _ = PreviewAsync(row.Match);
        };
        void ResetResults(string message)
        {
            scanVersion++;
            previewVersion++;
            results.SelectedItems.Clear();
            LiveText.SetOnce(results, "ItemsSource", localized => localized.ItemsSource = null);
            dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = false;
            all.IsEnabled = none.IsEnabled = false;
            LiveText.Set(selectedCount, "Text", localized => localized.Text = AppText.Get("NoResultsYet"));
            preview.Source = null;
            LiveText.Set(previewName, "Text", localized => localized.Text = AppText.Get("SelectAResultToPreviewTheImage"));
            LiveText.SetOnce(summary, "Text", localized => localized.Text = message);
        }
        void ConditionsChanged()
        {
            if (!active) return;
            ResetResults(AppText.Get("OptionsChangedSearchAgainToRefreshTheResults"));
            LiveText.Set(start, "Content", localized => localized.Content = hasSearched ? AppText.Get("SearchAgain") : AppText.Get("StartSearch"));
        }
        scope.SelectionChanged += (_, _) => ConditionsChanged();
        void MatchModeChanged(object sender, RoutedEventArgs args)
        {
            matchMode = nameOnly.IsChecked == true ? IdenticalImageMatchMode.NameOnly : IdenticalImageMatchMode.NameSizeContent;
            LiveText.Set(dialog, "Title", localized => localized.Title = nameOnly.IsChecked == true ? AppText.Get("FindAndRemoveImagesWithTheSameName") : AppText.Get("FindAndRemoveIdenticalImages"));
            LiveText.Set(description, "Text", localized => localized.Text = nameOnly.IsChecked == true
                ? AppText.Get("MatchesFileNamesIncludingExtensionsIgnoringCaseSizeAnd")
                : AppText.Get("MatchesNameSizeAndContentByDefaultIncludesSubfolders"));
            ConditionsChanged();
        }
        nameOnly.Checked += MatchModeChanged;
        nameOnly.Unchecked += MatchModeChanged;
        async Task ScanAsync(string searchFolder, IdenticalImageMatchMode searchMode, int request)
        {
            var reporter = new Progress<string>(text =>
            {
                if (active && searching && request == scanVersion) LiveText.SetOnce(summary, "Text", localized => localized.Text = text);
            });
            try
            {
                var found = await IdenticalImages.ScanAsync(searchFolder, targets.Select(target => target.Path).ToArray(), GalleryState.ShowHidden, reporter, scanCts.Token, mode: searchMode);
                if (!active || request != scanVersion) return;
                progress.Visibility = Visibility.Collapsed;
                LiveText.Set(summary, "Text", localized => localized.Text = AppText.Format("CheckedImagesFoundItems", found.Examined, found.Matches.Count)
                    + (found.Skipped > 0 ? AppText.Format("ItemsCouldNotBeRead", found.Skipped) : "")
                    + (found.Limited ? AppText.Format("ReachedTheItemLimitOnlyPartialResultsAreShown", IdenticalImages.MatchLimit) : ""));
                LiveText.SetOnce(results, "ItemsSource", localized => localized.ItemsSource = found.Matches.Select(match => new IdenticalImageRow(match)).ToArray());
                all.IsEnabled = none.IsEnabled = found.Matches.Count > 0;
                results.SelectAll();
                LiveText.Set(dialog, "CloseButtonText", localized => localized.CloseButtonText = AppText.Get("Close"));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (active && request == scanVersion) LiveText.Set(summary, "Text", localized => localized.Text = AppText.Format("SearchFailedAdjustTheOptionsAndTryAgain", ex.Message)); }
            finally
            {
                searching = false;
                if (active)
                {
                    progress.Visibility = Visibility.Collapsed;
                    scope.IsEnabled = start.IsEnabled = nameOnly.IsEnabled = true;
                    LiveText.Set(start, "Content", localized => localized.Content = AppText.Get("SearchAgain"));
                }
            }
        }
        Task? scanning = null;
        start.Click += (_, _) =>
        {
            if (searching || scope.SelectedItem is not string selectedFolder) return;
            searching = hasSearched = true;
            ResetResults(AppText.Get("ReadingSamplesAndSearching"));
            folder = selectedFolder;
            scope.IsEnabled = start.IsEnabled = nameOnly.IsEnabled = false;
            progress.Visibility = Visibility.Visible;
            scanning = ScanAsync(selectedFolder, matchMode, scanVersion);
        };
        _settingsDialog = dialog;
        Root.SizeChanged += OnRootSizeChanged;
        ContentDialogResult choice;
        try { choice = await dialog.ShowAsync(); }
        finally
        {
            active = false; previewVersion++; scanCts.Cancel(); preview.Source = null;
            Root.SizeChanged -= OnRootSizeChanged;
            content.SizeChanged -= UpdateCompactHeader;
            if (ReferenceEquals(_settingsDialog, dialog)) _settingsDialog = null;
            if (scanning is not null) await scanning;
        }
        if (choice is not (ContentDialogResult.Primary or ContentDialogResult.Secondary) || _closed || _closing) return;
        var selected = results.SelectedItems.Cast<IdenticalImageRow>().Select(row => row.Match).ToArray();
        if (selected.Length > 0) await CleanIdenticalImagesAsync(folder, selected, choice == ContentDialogResult.Secondary);
    }

    private static Grid CreateIdenticalImagesContent(UIElement header, ListView results, TextBlock selectedCount)
    {
        // 不设置 Height / MinHeight，让星号行按弹窗真正可用的空间分配列表视口。
        var content = new Grid { RowSpacing = 10, MaxHeight = 650, MaxWidth = 680 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(header); content.Children.Add(results); content.Children.Add(selectedCount);
        Grid.SetRow(results, 1); Grid.SetRow(selectedCount, 2);
        return content;
    }

    private ComboBox CreateIdenticalImageScope(IReadOnlyList<string> folders)
    {
        var scope = LiveText.Set(new PathComboBox
        {
            Header = AppText.Get("SearchLocationYouCanSelectAParentFolder"), ItemsSource = folders, SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = (DataTemplate)Root.Resources["FolderScopeTemplate"]
        }, "Header", localized => localized.Header = AppText.Get("SearchLocationYouCanSelectAParentFolder"));
        scope.SelectionChanged += (_, _) => LiveText.SetOnce(scope, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, scope.SelectedItem as string));
        LiveText.SetOnce(scope, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, scope.SelectedItem as string));
        return scope;
    }

    private static CheckBox CreateIdenticalImageMatchOption() => new()
    {
        Name = "IdenticalNameOnly", IsChecked = false,
        Content = LiveText.Set(new TextBlock { Text = AppText.Get("MatchFileNameOnlySizeAndContentMayDiffer"), TextWrapping = TextWrapping.Wrap }, "Text", localized => localized.Text = AppText.Get("MatchFileNameOnlySizeAndContentMayDiffer"))
    };

    private static void SizeIdenticalImagesContent(Grid content, double windowWidth, double windowHeight)
    {
        // 宽度只随宿主窗口变化，不能随选中路径或下拉打开时的临时内容变化收缩。
        // 100 DIP 预留弹窗左右内边距/边框及窗口边距；仍低于 760 DIP 的弹窗最大宽度。
        content.Width = content.MaxWidth = Math.Clamp(windowWidth - 100, 0, 680);
        // 高度保持可收缩，交给 ContentDialog 扣除标题和按钮，避免恢复之前的底部裁切问题。
        content.MaxHeight = Math.Clamp(windowHeight - 200, 0, 650);
    }

    private ContentDialog CreateIdenticalDeleteConfirmation(string folder, int count, bool permanent, IdenticalImageMatchMode mode) => new()
    {
        XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme,
        Title = mode == IdenticalImageMatchMode.NameOnly
            ? permanent ? AppText.Get("PermanentlyDeleteImagesWithTheSameName") : AppText.Get("RemoveImagesWithTheSameName")
            : permanent ? AppText.Get("PermanentlyDeleteIdenticalImages") : AppText.Get("RemoveIdenticalImages"),
        Content = LiveText.Set(new TextBlock
        {
            Text = (permanent ? AppText.Format("PermanentlyDeleteTheCheckedItemsTheyWillNotGo", count) : AppText.Format("MoveTheCheckedItemsToTheRecycleBin", count))
                + (mode == IdenticalImageMatchMode.NameOnly ? AppText.Get("MatchSameFileNameOnlySizeAndContentMay") : AppText.Get("MatchSameFileNameSizeAndContent"))
                + AppText.Format("LocationIncludesCheckedSampleFilesSavedWebPagesMay", folder),
            TextWrapping = TextWrapping.Wrap, MaxWidth = 460
        }, "Text", localized => localized.Text = (permanent ? AppText.Format("PermanentlyDeleteTheCheckedItemsTheyWillNotGo", count) : AppText.Format("MoveTheCheckedItemsToTheRecycleBin", count))
                + (mode == IdenticalImageMatchMode.NameOnly ? AppText.Get("MatchSameFileNameOnlySizeAndContentMay") : AppText.Get("MatchSameFileNameSizeAndContent"))
                + AppText.Format("LocationIncludesCheckedSampleFilesSavedWebPagesMay", folder)),
        PrimaryButtonText = permanent ? AppText.Get("DeletePermanently") : AppText.Get("MoveToRecycleBin"), CloseButtonText = AppText.Get("Cancel"), DefaultButton = ContentDialogButton.Close
    };

    private async Task CleanIdenticalImagesAsync(string folder, IReadOnlyList<IdenticalImageMatch> matches, bool permanent)
    {
        if (_deleting || _pasting || _closed || _closing) return;
        var mode = matches.Any(match => match.Mode == IdenticalImageMatchMode.NameOnly)
            ? IdenticalImageMatchMode.NameOnly : IdenticalImageMatchMode.NameSizeContent;
        var dialog = CreateIdenticalDeleteConfirmation(folder, matches.Count, permanent, mode);
        _settingsDialog = dialog;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_settingsDialog, dialog)) _settingsDialog = null; }
        if (result != ContentDialogResult.Primary || _closed || _closing) return;
        _deleting = true;
        _deleteSelectionReady = false;
        StopHoverPreview();
        var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var versions = new List<FileVersion>();
        int skipped = 0, failed = 0;
        string? firstError = null;
        double vertical = GalleryScroller.VerticalOffset, horizontal = GalleryScroller.HorizontalOffset;
        try
        {
            foreach (var match in matches)
            {
                if (_closed || _closing) break;
                try
                {
                    if ((!permanent && !SupportsRecycleBin(match.Path)) || !await IdenticalImages.IsUnchangedAsync(folder, match, _lifetimeCts.Token))
                    { skipped++; continue; }
                    await DeletePathAsync(match.Path, folder: false, recycle: !permanent);
                    deleted.Add(match.Path); versions.Add(match.Version);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { failed++; firstError ??= ex.Message; }
                if (deleted.Count % 25 == 0) GalleryState.Status = AppText.Format("RemovingIdenticalImagesItemsDeleted", deleted.Count);
            }
            if (!_closed)
            {
                var remaining = _selectedPaths.Where(path => !deleted.Contains(path)).ToArray();
                GalleryState.RemoveItems(deleted);
                RestoreItemSelection(remaining, remaining.FirstOrDefault(), remaining.FirstOrDefault());
                RestoreGalleryAfterDelete(horizontal, vertical);
            }
            string syncWarning = await SynchronizeDeletedDataAsync(deleted, versions);
            if (!_closed) GalleryState.Status = AppText.Format("ItemsSkippedFailed", (permanent ? AppText.Get("PermanentlyDeleted449197") : AppText.Get("MovedToRecycleBin04a803")), deleted.Count, skipped, failed)
                + (firstError is null ? "" : $"：{firstError}") + syncWarning;
        }
        finally
        {
            _deleting = _deleteSelectionReady = false;
            if (!_closed && !_closing && GalleryState.CurrentFolder is { } current) QueueFolderChange(current);
        }
    }
}
