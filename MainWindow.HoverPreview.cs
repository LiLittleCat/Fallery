using Fallery.Controls;
using Fallery.Models;
using Fallery.Services.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private MediaTile? _hoverPreviewTile;
    private IPlayer? _hoverPreviewPlayer;
    private CancellationTokenSource? _hoverPreviewCts;

    private void InitializeHoverPreview()
    {
        GalleryScroller.ViewChanging += (_, _) => StopHoverPreview();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) StopHoverPreview();
        };
        GalleryState.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GalleryState.IsScanning) && GalleryState.IsScanning) StopHoverPreview();
        };
    }

    private bool CanHoverPreview => !_closed && !_closing && !ViewerState.IsOpen && !IsFilmstrip
        && !GalleryState.IsScanning && _settingsDialog is null && _itemMenu is null
        && _addressFlyout is null && _gesturePointer is null && !_marqueeStarted;

    private void GalleryTile_PreviewHoverChanged(MediaTile tile, bool hovered)
    {
        if (!hovered)
        {
            if (ReferenceEquals(tile, _hoverPreviewTile)) StopHoverPreview();
            return;
        }
        if (!CanHoverPreview || tile.Item is not { IsVideo: true }) return;
        if (ReferenceEquals(tile, _hoverPreviewTile)) return;
        StopHoverPreview();
        _hoverPreviewTile = tile;
        _hoverPreviewCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _ = StartHoverPreviewAsync(tile, tile.Item, _hoverPreviewCts.Token);
    }

    private async Task StartHoverPreviewAsync(MediaTile tile, MediaItem item, CancellationToken ct)
    {
        try
        {
            await Task.Delay(350, ct);
            if (!CanHoverPreview || !ReferenceEquals(tile.Item, item) || !ReferenceEquals(_hoverPreviewTile, tile))
            {
                if (ReferenceEquals(_hoverPreviewTile, tile)) StopHoverPreview();
                return;
            }
            // 只有当前悬停项创建原生播放表面，不能给每张虚拟化卡片常驻一个播放器。
            var view = new MediaPlayerElement { AutoPlay = false, AreTransportControlsEnabled = false, IsHitTestVisible = false, Opacity = 0 };
            var player = new WindowsMediaPlayer(view, enableSystemTransportControls: false) { IsMuted = true, Volume = 0 };
            _hoverPreviewPlayer = player;
            tile.SetHoverPreview(view);
            player.Opened += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (!ct.IsCancellationRequested && ReferenceEquals(_hoverPreviewPlayer, player))
                    _ = RevealHoverPreviewAsync(player, view, ct);
            });
            player.Ended += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (ct.IsCancellationRequested || !ReferenceEquals(_hoverPreviewPlayer, player)) return;
                player.Position = TimeSpan.Zero;
                player.Play();
            });
            player.Failed += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(_hoverPreviewPlayer, player)) StopHoverPreview();
            });
            await player.OpenAsync(item.Path, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // 缺少解码器或文件不可用时保留静态封面，不干扰目录浏览。
            if (!ct.IsCancellationRequested && ReferenceEquals(_hoverPreviewTile, tile)) StopHoverPreview();
        }
    }

    private async Task RevealHoverPreviewAsync(IPlayer player, MediaPlayerElement view, CancellationToken ct)
    {
        try
        {
            await WaitForVideoLayoutFrameAsync(ct);
            if (ct.IsCancellationRequested || !ReferenceEquals(_hoverPreviewPlayer, player)) return;
            if (!CanHoverPreview) { StopHoverPreview(); return; }
            view.Opacity = 1;
            player.Play();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (ReferenceEquals(_hoverPreviewPlayer, player)) StopHoverPreview();
        }
    }

    private void StopHoverPreview()
    {
        // 移除播放表面会触发指针退出/卸载，先断开状态，避免重复进入释放过程。
        var tile = _hoverPreviewTile;
        _hoverPreviewTile = null;
        var cts = _hoverPreviewCts;
        _hoverPreviewCts = null;
        var player = _hoverPreviewPlayer;
        _hoverPreviewPlayer = null;
        cts?.Cancel();
        cts?.Dispose();
        player?.Dispose();
        tile?.SetHoverPreview(null);
    }
}
