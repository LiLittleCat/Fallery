using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Fallery;

public sealed partial class MainWindow
{
    private MediaSearchIndex? _searchIndex;
    private Task? _searchIndexTask;
    private CancellationTokenSource? _searchBuildCts, _searchQueryCts;
    private ContentDialog? _searchDialog;
    private TextBox? _searchInput;
    private ComboBox? _searchType;
    private ListView? _searchResults;
    private TextBlock? _searchProgress, _searchResultCount;
    private Button? _searchRefresh;
    private string _lastSearchText = "";

    public sealed record SearchRow(SearchEntry Entry, string Glyph)
    {
        public string Name => Entry.Name;
        public string Parent => Entry.Parent;
        public string Path => Entry.Path;
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await ShowSearchAsync();

    private async Task ShowSearchAsync()
    {
        if (_closed || _closing || _settingsDialog is not null || _deleting || _pickingFolder) return;
        if (_searchDialog is not null) { _searchInput?.Focus(FocusState.Programmatic); return; }
        StopHoverPreview();
        CloseItemMenu();
        CloseAddressMenu();
        EndAddressEdit(cancelRequest: true);
        SearchEntry? chosen = null;
        _searchInput = new TextBox { PlaceholderText = "输入名称、关键词或 *.jpg", Text = _lastSearchText, MaxLength = 512 };
        AutomationProperties.SetName(_searchInput, "全电脑文件名搜索");
        _searchType = new ComboBox { ItemsSource = new[] { "全部", "图片", "视频", "文件夹", "音频" }, SelectedIndex = 0, MinWidth = 96 };
        AutomationProperties.SetName(_searchType, "搜索结果类型");
        var inputRow = new Grid { ColumnSpacing = 8 };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.Children.Add(_searchInput); Grid.SetColumn(_searchType, 1); inputRow.Children.Add(_searchType);
        _searchResults = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true,
            Height = Math.Clamp(Root.ActualHeight - 340, 120, 420),
            ItemTemplate = (DataTemplate)Root.Resources["SearchResultTemplate"]
        };
        AutomationProperties.SetName(_searchResults, "搜索结果");
        _searchProgress = new TextBlock { Text = _searchIndex?.Status.Message ?? "首次搜索将建立本地磁盘索引", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        _searchResultCount = new TextBlock { Text = "搜索本地磁盘中的图片、视频、音频和文件夹", FontSize = 12, TextWrapping = TextWrapping.Wrap };
        _searchRefresh = new Button { Content = _searchIndex?.Status.Building == true ? "停止索引" : "刷新索引", Padding = new Thickness(10, 4, 10, 4) };
        var footer = new Grid { ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_searchProgress); Grid.SetColumn(_searchRefresh, 1); footer.Children.Add(_searchRefresh);
        var content = new StackPanel { Spacing = 12, Width = Math.Clamp(Root.ActualWidth - 96, 220, 680) };
        content.Children.Add(inputRow);
        content.Children.Add(new TextBlock { Text = "空格组合关键词 · ext:jpg;png · type:video · path:旅行 · ↑↓ 选择，Enter 打开", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(_searchResultCount); content.Children.Add(_searchResults); content.Children.Add(footer);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = "全电脑搜索",
            Content = content, CloseButtonText = "关闭", DefaultButton = ContentDialogButton.None
        };
        dialog.Resources["ContentDialogMaxWidth"] = 760d;
        _searchDialog = dialog;
        void Choose(SearchEntry entry) { chosen = entry; dialog.Hide(); }
        _searchResults.ItemClick += (_, e) => { if (e.ClickedItem is SearchRow row) Choose(row.Entry); };
        _searchInput.TextChanged += (_, _) => { _lastSearchText = _searchInput.Text; QueueSearch(); };
        _searchType.SelectionChanged += (_, _) => QueueSearch();
        content.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { e.Handled = true; dialog.Hide(); }
            else if (e.Key == VirtualKey.Enter && _searchResults.SelectedItem is SearchRow row) { e.Handled = true; Choose(row.Entry); }
            else if (e.Key is VirtualKey.Down or VirtualKey.Up && ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), _searchInput))
            {
                e.Handled = true;
                if (_searchResults.Items.Count == 0) return;
                _searchResults.SelectedIndex = Math.Clamp(_searchResults.SelectedIndex + (e.Key == VirtualKey.Down ? 1 : -1), 0, _searchResults.Items.Count - 1);
                _searchResults.ScrollIntoView(_searchResults.SelectedItem);
            }
            else if (e.Key == VirtualKey.F5) { e.Handled = true; RefreshSearchIndex(); }
        };
        _searchRefresh.Click += (_, _) => RefreshSearchIndex();
        dialog.Opened += (_, _) => { _searchInput.Focus(FocusState.Programmatic); _searchInput.SelectAll(); };
        EnsureSearchIndex();
        if (_searchIndexTask is null) _searchIndexTask = UpdateSearchIndexAsync(load: true);
        QueueSearch();
        try { await dialog.ShowAsync(); }
        finally
        {
            _searchQueryCts?.Cancel();
            _searchDialog = null;
            if (!_closed) SearchButton.Focus(FocusState.Programmatic);
        }
        if (chosen is null || _closed || _closing) return;
        try
        {
            if (!await Task.Run(() => File.Exists(chosen.Path) || Directory.Exists(chosen.Path)))
            { GalleryState.Status = "此结果已移动或删除，请刷新搜索索引"; return; }
            await OpenPathAsync(chosen.Path);
        }
        catch (Exception ex) { if (!_closed) GalleryState.Status = $"无法打开搜索结果：{ex.Message}"; }
    }

    private void RefreshSearchIndex()
    {
        if (_searchIndexTask is { IsCompleted: false }) _searchBuildCts?.Cancel();
        else _searchIndexTask = UpdateSearchIndexAsync(load: false);
    }

    private void EnsureSearchIndex()
    {
        if (_searchIndex is not null) return;
        _searchIndex = new MediaSearchIndex(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fallery", "Search", "media-v1.idx"));
        _searchIndex.Progress += status => DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || _searchDialog is null) return;
            _searchProgress!.Text = status.Message;
            _searchRefresh!.Content = status.Building ? "停止索引" : "刷新索引";
            QueueSearch();
        });
    }

    private async Task UpdateSearchIndexAsync(bool load)
    {
        if (_searchIndex is null) return;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _searchBuildCts = cts;
        try
        {
            if (load) await _searchIndex.LoadAsync(cts.Token);
            var roots = await Task.Run(MediaSearchIndex.LocalRoots, cts.Token);
            await _searchIndex.BuildAsync(roots, cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_closed && _searchDialog is not null) _searchProgress!.Text = "索引更新已停止，已有结果仍可搜索";
        }
        catch (Exception ex)
        {
            if (!_closed && _searchDialog is not null) _searchProgress!.Text = $"索引更新失败：{ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_searchBuildCts, cts)) _searchBuildCts = null;
            if (!_closed && _searchDialog is not null) _searchRefresh!.Content = "刷新索引";
        }
    }

    private void QueueSearch()
    {
        if (_searchDialog is null || _searchIndex is null) return;
        _searchQueryCts?.Cancel();
        string query = _searchInput!.Text;
        string prefix = _searchType!.SelectedIndex switch { 1 => "type:photo ", 2 => "type:video ", 3 => "type:folder ", 4 => "type:audio ", _ => "" };
        _ = RunSearchAsync(prefix + query);
    }

    private async Task RunSearchAsync(string query)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _searchQueryCts = cts;
        try
        {
            await Task.Delay(180, cts.Token);
            var matches = await _searchIndex!.SearchAsync(query, GalleryState.ShowHidden, cts.Token);
            if (_closed || cts.IsCancellationRequested || _searchDialog is null) return;
            _searchResults!.ItemsSource = matches.Items.Select(item => new SearchRow(item, FluentIcons.Glyph(item.Kind switch
            { MediaKind.Folder => "IconFolder", MediaKind.Video => "IconVideo", MediaKind.Audio => "IconAudio", _ => "IconImage" }))).ToArray();
            if (matches.Items.Count > 0) _searchResults.SelectedIndex = 0;
            _searchResultCount!.Text = string.IsNullOrWhiteSpace(query) ? "输入关键词，或选择类型浏览索引" :
                matches.Total == 0 ? (_searchIndex.Status.Building ? "暂未找到，索引仍在建立中…" : "没有匹配结果，可换关键词或刷新索引") :
                $"找到 {matches.Total:N0} 项" + (matches.Total > matches.Items.Count ? $"，显示前 {matches.Items.Count} 项，请缩小范围" : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed && !cts.IsCancellationRequested && _searchDialog is not null) _searchResultCount!.Text = $"搜索失败：{ex.Message}"; }
        finally { if (ReferenceEquals(_searchQueryCts, cts)) _searchQueryCts = null; }
    }
}
