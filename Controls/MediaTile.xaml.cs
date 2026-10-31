using System.ComponentModel;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.Controls;

public sealed partial class MediaTile : UserControl
{
    public MediaItem? Item { get; private set; }
    public bool IsSelected { get; private set; }
    public event Action<MediaTile, bool>? PreviewHoverChanged;
    private bool _showNames = true;
    private bool _showCard = true;

    public MediaTile()
    {
        InitializeComponent();
        Unloaded += (_, _) => PreviewHoverChanged?.Invoke(this, false);
    }

    public void SetHoverPreview(MediaPlayerElement? preview)
    {
        HoverVideoHost.Children.Clear();
        if (preview is not null)
        {
            preview.Stretch = PreviewImage.Stretch;
            HoverVideoHost.Children.Add(preview);
        }
    }

    public void SetPresentation(bool showNames, bool containImage, bool selected, bool showCard = true)
    {
        _showNames = showNames;
        _showCard = showCard;
        HoverOverlay.Margin = new Thickness(showCard ? -5 : 0);
        HoverOverlay.CornerRadius = new CornerRadius(showCard ? 7 : 0);
        // 平衡卡片底部的 5 像素内边距，让名称在图片下缘与卡片底边之间居中。
        MediaCaption.Margin = new Thickness(3, showCard ? 5 : 0, 3, 0);
        FolderFooter.Margin = new Thickness(2, showCard ? 5 : 0, 2, 0);
        CaptionRow.Height = new GridLength(showNames ? 22 : 0);
        MediaCaption.Visibility = showNames ? Visibility.Visible : Visibility.Collapsed;
        FolderFooter.Visibility = showNames && Item?.IsFolder == true ? Visibility.Visible : Visibility.Collapsed;
        PreviewImage.Stretch = containImage ? Microsoft.UI.Xaml.Media.Stretch.Uniform : Microsoft.UI.Xaml.Media.Stretch.UniformToFill;
        if (HoverVideoHost.Children.FirstOrDefault() is MediaPlayerElement preview) preview.Stretch = PreviewImage.Stretch;
        IsSelected = selected;
        UpdateMarks();
    }

    // 显示文件名时类型标记跟在名称行里；隐藏文件名时退回到图片角上的小徽标。
    private void UpdateMarks()
    {
        VideoBadge.Visibility = !_showNames && Item?.IsVideo == true ? Visibility.Visible : Visibility.Collapsed;
        VideoMark.Visibility = _showNames && Item?.IsVideo == true ? Visibility.Visible : Visibility.Collapsed;
        string kind = IsSelected ? "Selected" : "Image";
        VisualStateManager.GoToState(this, kind + (_showCard ? "Card" : "Plain"), false);
    }

    private void Tile_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        HoverOverlay.Visibility = Item is null ? Visibility.Collapsed : Visibility.Visible;
        Tile_PointerMoved(sender, e);
    }

    private void Tile_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        var buttons = e.GetCurrentPoint(this).Properties;
        PreviewHoverChanged?.Invoke(this, !buttons.IsLeftButtonPressed && !buttons.IsRightButtonPressed && !buttons.IsMiddleButtonPressed);
    }

    private void Tile_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        HoverOverlay.Visibility = Visibility.Collapsed;
        PreviewHoverChanged?.Invoke(this, false);
    }

    public void Bind(MediaItem? item)
    {
        PreviewHoverChanged?.Invoke(this, false);
        SetHoverPreview(null);
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
        ToolTipService.SetToolTip(this, item?.IsFolder == true ? item.Path : null);
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
        FrontCover.Width = covers.Count <= 1 ? 196 : 166;
        FrontCover.Height = covers.Count <= 1 ? 156 : 116;
        FrontCover.Margin = covers.Count <= 1 ? new Thickness(2, 2, 0, 0) : new Thickness(17, 38, 0, 0);
        FolderArtwork.Visibility = Item?.IsFolder == true ? Visibility.Visible : Visibility.Collapsed;
        FolderPlaceholder.Visibility = Item?.IsFolder == true && covers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FailureText.Text = Item?.IsVideo == true ? "无法生成预览\n双击尝试播放" : "无法加载预览";
        FailureText.Visibility = Item?.ThumbnailFailed == true ? Visibility.Visible : Visibility.Collapsed;
        LoadingText.Visibility = Item is { IsFolder: false, Thumbnail: null, ThumbnailFailed: false }
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
