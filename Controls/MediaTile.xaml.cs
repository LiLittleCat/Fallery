using System.ComponentModel;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.Controls;

public sealed partial class MediaTile : UserControl
{
    public MediaItem? Item { get; private set; }
    public bool IsSelected { get; private set; }
    private bool _showNames = true;
    private bool _showCard = true;

    public MediaTile() => InitializeComponent();

    public void SetPresentation(bool showNames, bool containImage, bool selected, bool showCard = true)
    {
        _showNames = showNames;
        _showCard = showCard;
        HoverOverlay.Margin = new Thickness(showCard ? -5 : 0);
        HoverOverlay.CornerRadius = new CornerRadius(showCard ? 7 : 0);
        CaptionRow.Height = new GridLength(showNames ? 22 : 0);
        MediaCaption.Visibility = showNames ? Visibility.Visible : Visibility.Collapsed;
        FolderFooter.Visibility = showNames && Item?.IsFolder == true ? Visibility.Visible : Visibility.Collapsed;
        PreviewImage.Stretch = containImage ? Microsoft.UI.Xaml.Media.Stretch.Uniform : Microsoft.UI.Xaml.Media.Stretch.UniformToFill;
        IsSelected = selected;
        UpdateMarks();
    }

    // 显示文件名时类型标记跟在名称行里；隐藏文件名时退回到图片角上的小徽标。
    private void UpdateMarks()
    {
        FolderBadge.Visibility = !_showNames && Item?.IsFolder == true ? Visibility.Visible : Visibility.Collapsed;
        VideoBadge.Visibility = !_showNames && Item?.IsVideo == true ? Visibility.Visible : Visibility.Collapsed;
        VideoMark.Visibility = _showNames && Item?.IsVideo == true ? Visibility.Visible : Visibility.Collapsed;
        string kind = IsSelected ? "Selected" : Item?.IsVideo == true ? "Video" : "Image";
        VisualStateManager.GoToState(this, kind + (_showCard ? "Card" : "Plain"), false);
    }

    private void Tile_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        HoverOverlay.Visibility = Item is null ? Visibility.Collapsed : Visibility.Visible;

    private void Tile_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        HoverOverlay.Visibility = Visibility.Collapsed;

    public void Bind(MediaItem? item)
    {
        // 回收卡片可能还保留旧 DataTemplate 状态；每次准备都完整覆盖，避免跨目录残留。
        if (Item is not null) Item.PropertyChanged -= Item_PropertyChanged;
        Item = item;
        IsSelected = false;
        HoverOverlay.Visibility = Visibility.Collapsed;
        MediaName.Text = item is { IsFolder: false } ? item.Name : string.Empty;
        FolderName.Text = item?.IsFolder == true ? item.Name : string.Empty;
        FolderPlaceholder.Visibility = FolderFooter.Visibility = item?.IsFolder == true
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateMarks();
        ToolTipService.SetToolTip(this, item?.Path);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, item?.Name ?? string.Empty);
        UpdatePreview();
        if (Item is not null) Item.PropertyChanged += Item_PropertyChanged;
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Item) && e.PropertyName is nameof(MediaItem.Thumbnail) or nameof(MediaItem.ThumbnailFailed) or nameof(MediaItem.FolderThumbnails))
            UpdatePreview();
    }

    private void UpdatePreview()
    {
        PreviewImage.Source = Item?.IsFolder == true ? null : Item?.Thumbnail;
        var covers = Item?.IsFolder == true ? Item.FolderThumbnails : [];
        FrontCoverImage.Source = covers.ElementAtOrDefault(0);
        MiddleCoverImage.Source = covers.ElementAtOrDefault(1);
        BackCoverImage.Source = covers.ElementAtOrDefault(2);
        FolderStack.Visibility = covers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MiddleCover.Visibility = covers.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        BackCover.Visibility = covers.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
        FolderPlaceholder.Visibility = Item?.IsFolder == true && covers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FailureText.Text = Item?.IsVideo == true ? "无法生成预览\n双击尝试播放" : "无法加载预览";
        FailureText.Visibility = Item?.ThumbnailFailed == true ? Visibility.Visible : Visibility.Collapsed;
        LoadingText.Visibility = Item is { IsFolder: false, Thumbnail: null, ThumbnailFailed: false }
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
