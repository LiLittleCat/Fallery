using Fallery.Controls;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Fallery;

public sealed partial class MainWindow
{
    private const double FilmstripTileWidth = 120;
    private readonly StackLayout _filmstripLayout = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private bool IsFilmstrip => GalleryState.DisplayMode == GalleryDisplayMode.Filmstrip;

    public Visibility GalleryChromeVisibility(bool open, GalleryDisplayMode mode) =>
        ToVisibility(!open || mode == GalleryDisplayMode.Filmstrip);
    public Visibility ViewerChromeVisibility(bool open, GalleryDisplayMode mode) =>
        ToVisibility(open && mode != GalleryDisplayMode.Filmstrip);
    public Visibility FilmstripVisibility(GalleryDisplayMode mode) => ToVisibility(mode == GalleryDisplayMode.Filmstrip);

    public string DisplayModeGlyph(GalleryDisplayMode mode) => FluentIcons.Glyph(mode switch
    {
        GalleryDisplayMode.Justified => "IconJustified",
        GalleryDisplayMode.LargeIcons => "IconLargeIcons",
        GalleryDisplayMode.Filmstrip => "IconFilmstrip",
        _ => "IconMasonry"
    });

    private async void DisplayMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string text } && Enum.TryParse<GalleryDisplayMode>(text, out var mode))
            await SetDisplayModeAsync(mode);
    }

    private async Task SetDisplayModeAsync(GalleryDisplayMode mode)
    {
        if (_closed || _closing || !Enum.IsDefined(mode)) return;
        int selected = Math.Max(0, ViewerState.CurrentIndex);
        CloseItemMenu();
        CloseViewer();
        GalleryState.DisplayMode = mode;
        ApplyDisplayOptions();
        QueueSettingsSave();
        if (IsFilmstrip) await ShowInViewerAsync(selected);
    }

    private void ShowCards_Click(object sender, RoutedEventArgs e)
    {
        GalleryState.ShowCards = ShowCardsItem.IsChecked;
        ApplyDisplayOptions();
        QueueSettingsSave();
    }

    private void ShowFileNames_Click(object sender, RoutedEventArgs e)
    {
        GalleryState.ShowFileNames = ShowFileNamesItem.IsChecked;
        ApplyDisplayOptions();
        QueueSettingsSave();
    }

    private void PlaceViewer()
    {
        bool embedded = IsFilmstrip && _presenterBeforeFullScreen is null;
        var target = embedded ? GalleryContent : Root;
        if (Viewer.Parent is Panel parent && parent != target)
        {
            parent.Children.Remove(Viewer);
            target.Children.Add(Viewer);
        }
        Grid.SetRow(Viewer, embedded || _presenterBeforeFullScreen is not null ? 0 : 1);
        Grid.SetRowSpan(Viewer, embedded ? 1 : _presenterBeforeFullScreen is not null ? 3 : 2);
        UpdateViewerBackdrop();
    }

    private void UpdateViewerBackdrop()
    {
        bool embedded = IsFilmstrip && _presenterBeforeFullScreen is null;
        bool covered = ViewerState.IsOpen && !embedded;
        // 保留浏览区布局和滚动位置，但不能让旧缩略图透过查看器的透明留白。
        BrowserSurface.Opacity = BrowserStatus.Opacity = covered ? 0 : 1;
        BrowserSurface.IsHitTestVisible = BrowserStatus.IsHitTestVisible = !covered;
        GallerySurfaceFill.Opacity = ViewerState.IsOpen && embedded ? 0 : 1;
    }

    private void ApplyDisplayOptions()
    {
        // 换布局时先回收原控件，避免横排固定尺寸残留到瀑布流复用的卡片上。
        ReleaseGalleryThumbnails();
        Gallery.ItemsSource = null;
        Masonry.UniformTiles = GalleryState.DisplayMode == GalleryDisplayMode.LargeIcons;
        Masonry.JustifiedRows = GalleryState.DisplayMode == GalleryDisplayMode.Justified;
        Masonry.ShowFileNames = GalleryState.ShowFileNames;
        Masonry.ShowCards = GalleryState.ShowCards;
        Masonry.Refresh();
        Gallery.Layout = IsFilmstrip ? _filmstripLayout : Masonry;
        Grid.SetRow(GalleryScroller, IsFilmstrip ? 1 : 0);
        GalleryScroller.Height = IsFilmstrip ? GalleryState.ShowFileNames ? 154 : 132 : double.NaN;
        GalleryScroller.HorizontalScrollMode = IsFilmstrip ? ScrollMode.Enabled : ScrollMode.Disabled;
        GalleryScroller.HorizontalScrollBarVisibility = IsFilmstrip ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        GalleryScroller.VerticalScrollMode = IsFilmstrip ? ScrollMode.Disabled : ScrollMode.Enabled;
        GalleryScroller.VerticalScrollBarVisibility = IsFilmstrip ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        PlaceViewer();
        ApplyPlaylistLayout();
        Gallery.ItemsSource = GalleryState.Items;
        GalleryScroller.ChangeView(0, 0, null, true);
        JustifiedModeItem.IsChecked = GalleryState.DisplayMode == GalleryDisplayMode.Justified;
        MasonryModeItem.IsChecked = GalleryState.DisplayMode == GalleryDisplayMode.Masonry;
        LargeIconsModeItem.IsChecked = GalleryState.DisplayMode == GalleryDisplayMode.LargeIcons;
        FilmstripModeItem.IsChecked = IsFilmstrip;
        ShowFileNamesItem.IsChecked = GalleryState.ShowFileNames;
        ShowCardsItem.IsChecked = GalleryState.ShowCards;
        ThumbSize.IsEnabled = !IsFilmstrip;
        UpdateGalleryVisibility();
        UpdateFilmstripSelection();
    }

    private void ConfigureTile(MediaTile tile)
    {
        tile.Width = IsFilmstrip ? FilmstripTileWidth : double.NaN;
        tile.Height = IsFilmstrip ? GalleryState.ShowFileNames ? 132 : 110 : double.NaN;
        // 只有网格和横排的格子比例与图片不一致，需要完整显示；瀑布流与自适应行按原比例铺满。
        tile.SetPresentation(GalleryState.ShowFileNames, GalleryState.DisplayMode is GalleryDisplayMode.LargeIcons or GalleryDisplayMode.Filmstrip,
            tile.Item is not null && ReferenceEquals(tile.Item, IsFilmstrip ? ViewerState.CurrentItem : _selectedItem),
            GalleryState.ShowCards);
    }

    /// <summary>瀑布流和大图列表的单击选中；横排模式的选中始终跟随当前大图。</summary>
    private void SelectItem(MediaItem? item)
    {
        if (ReferenceEquals(_selectedItem, item)) return;
        _selectedItem = item;
        if (IsFilmstrip || _closed) return;
        foreach (var tile in _realizedItems.Keys.OfType<MediaTile>()) ConfigureTile(tile);
    }

    private void UpdateFilmstripSelection()
    {
        if (!IsFilmstrip || _closed) return;
        foreach (var tile in _realizedItems.Keys.OfType<MediaTile>()) ConfigureTile(tile);
        if (ViewerState.CurrentItem is not { } item) return;
        int index = GalleryState.Items.IndexOf(item);
        if (index < 0) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || !IsFilmstrip || !ReferenceEquals(item, ViewerState.CurrentItem)) return;
            GalleryScroller.UpdateLayout();
            double left = index * 128;
            if (left < GalleryScroller.HorizontalOffset || left + 120 > GalleryScroller.HorizontalOffset + GalleryScroller.ViewportWidth)
                GalleryScroller.ChangeView(Math.Max(0, left - (GalleryScroller.ViewportWidth - 120) / 2), null, null, true);
        });
    }

    private async void Tile_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // 点击卡片后让键盘操作跟随浏览区，避免焦点仍留在之前的文件树节点。
        if (sender is MediaTile tile) tile.Focus(FocusState.Pointer);
        if (!IsFilmstrip)
        {
            if (sender is MediaTile { Item: { } selected }) SelectItem(selected);
            return;
        }
        if (sender is not MediaTile { Item: { IsFolder: false } item }) return;
        e.Handled = true;
        await ShowInViewerAsync(GalleryState.MediaItems.IndexOf(item));
    }
}
