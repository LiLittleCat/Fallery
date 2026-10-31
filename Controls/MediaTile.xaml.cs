using System.ComponentModel;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.Controls;

public sealed partial class MediaTile : UserControl
{
    public MediaItem? Item { get; private set; }

    public MediaTile() => InitializeComponent();

    public void Bind(MediaItem? item)
    {
        // 回收卡片可能还保留旧 DataTemplate 状态；每次准备都完整覆盖，避免跨目录残留。
        if (Item is not null) Item.PropertyChanged -= Item_PropertyChanged;
        Item = item;
        MediaName.Text = item is { IsFolder: false } ? item.Name : string.Empty;
        FolderName.Text = item?.IsFolder == true ? item.Name : string.Empty;
        FolderPlaceholder.Visibility = FolderFooter.Visibility = item?.IsFolder == true
            ? Visibility.Visible : Visibility.Collapsed;
        VideoBadge.Visibility = item?.IsVideo == true ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(this, item?.Path);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, item?.Name ?? string.Empty);
        UpdatePreview();
        if (Item is not null) Item.PropertyChanged += Item_PropertyChanged;
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Item) && e.PropertyName is nameof(MediaItem.Thumbnail) or nameof(MediaItem.ThumbnailFailed))
            UpdatePreview();
    }

    private void UpdatePreview()
    {
        PreviewImage.Source = Item?.Thumbnail;
        FailureText.Text = Item?.IsVideo == true ? "无法生成预览\n双击尝试播放" : "无法加载预览";
        FailureText.Visibility = Item?.ThumbnailFailed == true ? Visibility.Visible : Visibility.Collapsed;
        LoadingText.Visibility = Item is { IsFolder: false, Thumbnail: null, ThumbnailFailed: false }
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
