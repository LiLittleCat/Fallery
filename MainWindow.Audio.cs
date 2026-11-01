using Fallery.Models;
using Microsoft.UI.Xaml;

namespace Fallery;

public sealed partial class MainWindow
{
    private async Task LoadAudioArtworkAsync(MediaItem item, CancellationToken ct)
    {
        await item.EnsureThumbnailAsync(512);
        if (_closed || ct.IsCancellationRequested || _videoPlayer is null || !ReferenceEquals(ViewerState.CurrentItem, item)) return;
        AudioArtwork.Source = item.Thumbnail;
        AudioPlaceholder.Visibility = item.Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
