using Fallery.Layouts;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private void GalleryScroller_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateDetailsWidth();

    private void GalleryScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (IsDetailsView) DetailsHeaderScroller.ChangeView(GalleryScroller.HorizontalOffset, null, null, true);
    }

    private void UpdateDetailsWidth()
    {
        if (Gallery is null || DetailsHeaderColumns is null) return;
        double viewport = GalleryScroller.ViewportWidth > 0 ? GalleryScroller.ViewportWidth : GalleryScroller.ActualWidth;
        Gallery.Width = IsDetailsView ? Math.Max(MasonryGeometry.DetailsMinWidth, viewport - 24) : double.NaN;
        DetailsHeaderColumns.Width = IsDetailsView ? Gallery.Width : double.NaN;
    }

    private async void DetailsSort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<MediaSortField>(tag, out var field)) return;
        await SetDetailsSortAsync(field);
    }

    private async Task SetDetailsSortAsync(MediaSortField field)
    {
        GalleryState.Descending = GalleryState.SortField == field && !GalleryState.Descending;
        GalleryState.SortField = field;
        UpdateBrowseMenus();
        UpdateDetailsHeaders();
        QueueSettingsSave();
        await ApplySortAsync();
    }

    private void UpdateDetailsHeaders()
    {
        if (DetailsNameHeader is null) return;
        foreach (var (button, label, field) in new[]
        {
            (DetailsNameHeader, "名称", MediaSortField.Name), (DetailsSizeHeader, "大小", MediaSortField.Size),
            (DetailsCreatedHeader, "创建时间", MediaSortField.Created), (DetailsModifiedHeader, "修改时间", MediaSortField.Modified)
        }) button.Content = label + (GalleryState.SortField == field ? GalleryState.Descending ? " ↓" : " ↑" : "");
    }
}
