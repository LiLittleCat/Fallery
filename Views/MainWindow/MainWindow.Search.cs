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
    private Action<SearchIndexStatus>? _searchProgressHandler;
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
        _searchInput = LiveText.Set(new TextBox { PlaceholderText = AppText.Get("EnterANameKeywordsOrJpg"), Text = _lastSearchText, MaxLength = 512 }, "PlaceholderText", localized => localized.PlaceholderText = AppText.Get("EnterANameKeywordsOrJpg"));
        LiveText.Set(_searchInput, "AutomationProperties.Name", localized => AutomationProperties.SetName(localized, AppText.Get("SearchFileNamesOnThisComputer")));
        _searchType = LiveText.Set(new ComboBox { ItemsSource = new[] { AppText.Get("All"), AppText.Get("Photos"), AppText.Get("Videos"), AppText.Get("Folder"), AppText.Get("Audio") }, SelectedIndex = 0, MinWidth = 96 }, "ItemsSource", localized => localized.ItemsSource = new[] { AppText.Get("All"), AppText.Get("Photos"), AppText.Get("Videos"), AppText.Get("Folder"), AppText.Get("Audio") });
        LiveText.Set(_searchType, "AutomationProperties.Name", localized => AutomationProperties.SetName(localized, AppText.Get("SearchResultType")));
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
        LiveText.Set(_searchResults, "AutomationProperties.Name", localized => AutomationProperties.SetName(localized, AppText.Get("SearchResults")));
        _searchProgress = LiveText.Set(new TextBlock { Text = _searchIndex?.Status.Message ?? AppText.Get("TheFirstSearchWillBuildALocalDriveIndex"), TextWrapping = TextWrapping.Wrap, FontSize = 12 }, "Text", localized => localized.Text = _searchIndex?.Status.Message ?? AppText.Get("TheFirstSearchWillBuildALocalDriveIndex"));
        _searchResultCount = LiveText.Set(new TextBlock { Text = AppText.Get("SearchLocalDrivesForPhotosVideosAudioAndFolders"), FontSize = 12, TextWrapping = TextWrapping.Wrap }, "Text", localized => localized.Text = AppText.Get("SearchLocalDrivesForPhotosVideosAudioAndFolders"));
        _searchRefresh = LiveText.Set(new Button { Content = _searchIndex?.Status.Building == true ? AppText.Get("StopIndexing") : AppText.Get("RefreshIndex"), Padding = new Thickness(10, 4, 10, 4) }, "Content", localized => localized.Content = _searchIndex?.Status.Building == true ? AppText.Get("StopIndexing") : AppText.Get("RefreshIndex"));
        var footer = new Grid { ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_searchProgress); Grid.SetColumn(_searchRefresh, 1); footer.Children.Add(_searchRefresh);
        var content = new StackPanel { Spacing = 12, Width = Math.Clamp(Root.ActualWidth - 96, 220, 680) };
        content.Children.Add(inputRow);
        content.Children.Add(LiveText.Set(new TextBlock { Text = AppText.Get("CombineKeywordsWithSpacesExtJpgPngTypeVideo"), FontSize = 12, TextWrapping = TextWrapping.Wrap }, "Text", localized => localized.Text = AppText.Get("CombineKeywordsWithSpacesExtJpgPngTypeVideo")));
        content.Children.Add(_searchResultCount); content.Children.Add(_searchResults); content.Children.Add(footer);
        var dialog = LiveText.Set(LiveText.Set(new ContentDialog
        {
            XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = AppText.Get("SearchThisComputer"),
            Content = content, CloseButtonText = AppText.Get("Close"), DefaultButton = ContentDialogButton.None
        }, "Title", localized => localized.Title = AppText.Get("SearchThisComputer")), "CloseButtonText", localized => localized.CloseButtonText = AppText.Get("Close"));
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
            { GalleryState.Status = AppText.Get("ThisItemWasMovedOrDeletedPleaseRefreshThe"); return; }
            await OpenPathAsync(chosen.Path);
        }
        catch (Exception ex) { if (!_closed) GalleryState.Status = AppText.Format("CouldNotOpenSearchResult", ex.Message); }
    }

    private void RefreshSearchIndex()
    {
        if (_searchIndexTask is { IsCompleted: false }) _searchBuildCts?.Cancel();
        else _searchIndexTask = UpdateSearchIndexAsync(load: false);
    }

    private void EnsureSearchIndex()
    {
        if (_searchIndex is not null) return;
        _searchIndex = MediaSearchIndex.Shared;
        _searchProgressHandler = status => DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || _searchDialog is null) return;
            LiveText.Set(_searchProgress!, "Text", localized => localized.Text = _searchIndex!.Status.DisplayMessage);
            LiveText.Set(_searchRefresh!, "Content", localized => localized.Content = status.Building ? AppText.Get("StopIndexing") : AppText.Get("RefreshIndex"));
            QueueSearch();
        });
        _searchIndex.Progress += _searchProgressHandler;
    }

    private async Task UpdateSearchIndexAsync(bool load)
    {
        if (_searchIndex is null) return;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _searchBuildCts = cts;
        try
        {
            if (load) await _searchIndex.EnsureLoadedAsync(cts.Token);
            if (_searchIndex.Status.Building) return;
            var roots = await Task.Run(MediaSearchIndex.LocalRoots, cts.Token);
            await _searchIndex.BuildAsync(roots, cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_closed && _searchDialog is not null) LiveText.Set(_searchProgress!, "Text", localized => localized.Text = AppText.Get("IndexingStoppedExistingResultsRemainSearchable"));
        }
        catch (Exception ex)
        {
            if (!_closed && _searchDialog is not null) LiveText.Set(_searchProgress!, "Text", localized => localized.Text = AppText.Format("IndexUpdateFailed", ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_searchBuildCts, cts)) _searchBuildCts = null;
            if (!_closed && _searchDialog is not null) LiveText.Set(_searchRefresh!, "Content", localized => localized.Content = AppText.Get("RefreshIndex"));
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
            LiveText.SetOnce(_searchResults!, "ItemsSource", localized => localized.ItemsSource = matches.Items.Select(item => new SearchRow(item, FluentIcons.Glyph(item.Kind switch
            { MediaKind.Folder => "IconFolder", MediaKind.Video => "IconVideo", MediaKind.Audio => "IconAudio", _ => "IconImage" }))).ToArray());
            if (matches.Items.Count > 0) _searchResults!.SelectedIndex = 0;
            LiveText.Set(_searchResultCount!, "Text", localized => localized.Text = string.IsNullOrWhiteSpace(query) ? AppText.Get("EnterKeywordsOrSelectATypeToBrowseThe") :
                matches.Total == 0 ? (_searchIndex.Status.Building ? AppText.Get("NoResultsYetIndexingIsStillInProgress") : AppText.Get("NoMatchingResultsTryDifferentKeywordsOrRefreshThe")) :
                AppText.Format("FoundItems", matches.Total) + (matches.Total > matches.Items.Count ? AppText.Format("ShowingTheFirstNarrowYourSearch", matches.Items.Count) : ""));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed && !cts.IsCancellationRequested && _searchDialog is not null) LiveText.Set(_searchResultCount!, "Text", localized => localized.Text = AppText.Format("SearchFailed", ex.Message)); }
        finally { if (ReferenceEquals(_searchQueryCts, cts)) _searchQueryCts = null; }
    }
}
