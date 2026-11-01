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
        public string Name => $"{Match.Name}  ·  {(Match.Mode == IdenticalImageMatchMode.NameOnly ? "仅文件名相同" : "名称、大小和内容相同")}";
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
        { GalleryState.Status = "相同图片清理只支持可使用回收站的本地固定磁盘"; return; }
        StopHoverPreview(); CloseItemMenu(); CloseAddressMenu();
        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        bool active = true;
        var summary = new TextBlock { Text = "选择扫描范围后开始查找。", TextWrapping = TextWrapping.Wrap };
        var selectedCount = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
        var ancestors = new List<string>();
        for (string? parent = folder; parent is not null; parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(parent))) ancestors.Add(parent);
        var scope = CreateIdenticalImageScope(ancestors);
        var nameOnly = CreateIdenticalImageMatchOption();
        var matchMode = IdenticalImageMatchMode.NameSizeContent;
        var start = new Button { Content = "开始查找" };
        var preview = new Image { Width = 96, Height = 80, Stretch = Stretch.Uniform };
        var previewName = new TextBlock { Text = "选择结果可预览图片", TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3, MaxWidth = 430 };
        var previewPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        previewPanel.Children.Add(preview); previewPanel.Children.Add(previewName);
        var results = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            ItemTemplate = (DataTemplate)Root.Resources["IdenticalImageResultTemplate"]
        };
        var all = new Button { Content = "全选", IsEnabled = false };
        var none = new Button { Content = "取消全选", IsEnabled = false };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(all); actions.Children.Add(none);
        all.Click += (_, _) => results.SelectAll(); none.Click += (_, _) => results.SelectedItems.Clear();
        var scopeRow = new Grid { ColumnSpacing = 8 };
        scopeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scopeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        scopeRow.Children.Add(scope); scopeRow.Children.Add(start); Grid.SetColumn(start, 1);
        start.VerticalAlignment = VerticalAlignment.Bottom;
        var description = new TextBlock { Text = "默认匹配文件名、大小和内容。包含子文件夹和样本本身，可取消勾选要保留的项。", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
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
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = "查找并清理相同图片",
            Content = content,
            PrimaryButtonText = "移到回收站", IsPrimaryButtonEnabled = false,
            SecondaryButtonText = "彻底删除", IsSecondaryButtonEnabled = false,
            CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        dialog.Resources["ContentDialogMaxWidth"] = 760d;
        int previewVersion = 0;
        int scanVersion = 0;
        bool searching = false, hasSearched = false;
        var previewSlot = new SemaphoreSlim(1);
        async Task PreviewAsync(IdenticalImageMatch match)
        {
            int version = ++previewVersion;
            preview.Source = null; previewName.Text = match.Path;
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
            catch (Exception) { if (active && version == previewVersion) previewName.Text = $"预览不可用：{match.Path}"; }
            finally { previewSlot.Release(); }
        }
        results.SelectionChanged += (_, args) =>
        {
            dialog.IsPrimaryButtonEnabled = results.SelectedItems.Count > 0;
            dialog.IsSecondaryButtonEnabled = results.SelectedItems.Count > 0;
            selectedCount.Text = $"已选择 {results.SelectedItems.Count:N0} 项（包括勾选的样本）";
            if (args.AddedItems.OfType<IdenticalImageRow>().LastOrDefault() is { } row) _ = PreviewAsync(row.Match);
        };
        void ResetResults(string message)
        {
            scanVersion++;
            previewVersion++;
            results.SelectedItems.Clear();
            results.ItemsSource = null;
            dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = false;
            all.IsEnabled = none.IsEnabled = false;
            selectedCount.Text = "尚无本轮结果";
            preview.Source = null;
            previewName.Text = "选择结果可预览图片";
            summary.Text = message;
        }
        void ConditionsChanged()
        {
            if (!active) return;
            ResetResults("条件已更改，点击查找以更新结果。");
            start.Content = hasSearched ? "重新查找" : "开始查找";
        }
        scope.SelectionChanged += (_, _) => ConditionsChanged();
        void MatchModeChanged(object sender, RoutedEventArgs args)
        {
            matchMode = nameOnly.IsChecked == true ? IdenticalImageMatchMode.NameOnly : IdenticalImageMatchMode.NameSizeContent;
            dialog.Title = nameOnly.IsChecked == true ? "查找并清理同名图片" : "查找并清理相同图片";
            description.Text = nameOnly.IsChecked == true
                ? "仅文件名（含扩展名，不区分大小写）相同即可列入；大小和内容可能不同。包含子文件夹和样本本身。"
                : "默认匹配文件名、大小和内容。包含子文件夹和样本本身，可取消勾选要保留的项。";
            ConditionsChanged();
        }
        nameOnly.Checked += MatchModeChanged;
        nameOnly.Unchecked += MatchModeChanged;
        async Task ScanAsync(string searchFolder, IdenticalImageMatchMode searchMode, int request)
        {
            var reporter = new Progress<string>(text =>
            {
                if (active && searching && request == scanVersion) summary.Text = text;
            });
            try
            {
                var found = await IdenticalImages.ScanAsync(searchFolder, targets.Select(target => target.Path).ToArray(), GalleryState.ShowHidden, reporter, scanCts.Token, mode: searchMode);
                if (!active || request != scanVersion) return;
                progress.Visibility = Visibility.Collapsed;
                summary.Text = $"检查 {found.Examined:N0} 张图片，找到 {found.Matches.Count:N0} 项"
                    + (found.Skipped > 0 ? $"；{found.Skipped:N0} 项不可读取" : "")
                    + (found.Limited ? $"。已到 {IdenticalImages.MatchLimit:N0} 项上限，本次仅列出部分结果。" : "");
                results.ItemsSource = found.Matches.Select(match => new IdenticalImageRow(match)).ToArray();
                all.IsEnabled = none.IsEnabled = found.Matches.Count > 0;
                results.SelectAll();
                dialog.CloseButtonText = "关闭";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (active && request == scanVersion) summary.Text = $"扫描失败：{ex.Message}。可调整条件后重新查找。"; }
            finally
            {
                searching = false;
                if (active)
                {
                    progress.Visibility = Visibility.Collapsed;
                    scope.IsEnabled = start.IsEnabled = nameOnly.IsEnabled = true;
                    start.Content = "重新查找";
                }
            }
        }
        Task? scanning = null;
        start.Click += (_, _) =>
        {
            if (searching || scope.SelectedItem is not string selectedFolder) return;
            searching = hasSearched = true;
            ResetResults("正在读取样本并扫描…");
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
        var scope = new PathComboBox
        {
            Header = "扫描范围（可选择上级目录）", ItemsSource = folders, SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = (DataTemplate)Root.Resources["FolderScopeTemplate"]
        };
        scope.SelectionChanged += (_, _) => ToolTipService.SetToolTip(scope, scope.SelectedItem as string);
        ToolTipService.SetToolTip(scope, scope.SelectedItem as string);
        return scope;
    }

    private static CheckBox CreateIdenticalImageMatchOption() => new()
    {
        Name = "IdenticalNameOnly", IsChecked = false,
        Content = new TextBlock { Text = "仅匹配文件名（大小和内容可不同）", TextWrapping = TextWrapping.Wrap }
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
            ? permanent ? "确认彻底删除同名图片" : "确认清理同名图片"
            : permanent ? "确认彻底删除相同图片" : "确认清理相同图片",
        Content = new TextBlock
        {
            Text = (permanent ? $"彻底删除勾选的 {count:N0} 项？不会放入回收站，无法从回收站恢复。" : $"将勾选的 {count:N0} 项移到回收站？")
                + (mode == IdenticalImageMatchMode.NameOnly ? "\n匹配方式：仅文件名相同，大小和内容可能不同。" : "\n匹配方式：文件名、大小和内容均相同。")
                + $"\n\n范围：{folder}\n包括勾选的样本文件。保存的网页可能缺少对应图标。扫描后发生变化的文件会跳过。",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 460
        },
        PrimaryButtonText = permanent ? "彻底删除" : "移到回收站", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
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
                if (deleted.Count % 25 == 0) GalleryState.Status = $"正在清理相同图片：已删除 {deleted.Count:N0} 项…";
            }
            if (!_closed)
            {
                var remaining = _selectedPaths.Where(path => !deleted.Contains(path)).ToArray();
                GalleryState.RemoveItems(deleted);
                RestoreItemSelection(remaining, remaining.FirstOrDefault(), remaining.FirstOrDefault());
                RestoreGalleryAfterDelete(horizontal, vertical);
            }
            string syncWarning = await SynchronizeDeletedDataAsync(deleted, versions);
            if (!_closed) GalleryState.Status = $"{(permanent ? "已彻底删除" : "已移到回收站")} {deleted.Count:N0} 项，跳过 {skipped:N0} 项，失败 {failed:N0} 项"
                + (firstError is null ? "" : $"：{firstError}") + syncWarning;
        }
        finally
        {
            _deleting = _deleteSelectionReady = false;
            if (!_closed && !_closing && GalleryState.CurrentFolder is { } current) QueueFolderChange(current);
        }
    }
}
