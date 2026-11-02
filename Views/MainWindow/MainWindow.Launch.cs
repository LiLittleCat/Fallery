using System.Runtime.InteropServices;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Fallery;

public sealed partial class MainWindow
{
    private readonly string? _startupPath;
    // “打开方式”打开的文件：目录扫描完成后在浏览区选中它，并沿用正在播放的项。
    private string? _launchPath;

    /// <summary>已开窗口收到另一个进程转来的请求：切到前台，有路径就打开。</summary>
    public void ActivateFromRedirect(string? path)
    {
        if (_closed || _closing) return;
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (path is not null) _ = OpenPathAsync(path);
    }

    /// <summary>先播放，再在后台扫描所在目录和定位文件树，保证“打开方式”尽快出画面。</summary>
    private async Task OpenPathAsync(string path)
    {
        if (_closed || _closing) return;
        if (Directory.Exists(path))
        {
            await NavigateAsync(path);
            return;
        }
        if (System.IO.Path.GetDirectoryName(path) is not { } folder) return;
        if (FolderScanner.GetMediaKind(path) is not { } kind)
        {
            await NavigateAsync(folder);
            return;
        }
        _settingsDialog?.Hide();
        if (!FolderScanner.MatchesFilter(kind, GalleryState.Filter))
        {
            // 明确打开的文件必须在播放列表里，筛选临时放开为所有媒体。
            GalleryState.Filter = MediaFilter.All;
            UpdateBrowseMenus();
            QueueSettingsSave();
        }
        _launchPath = path;
        var item = new MediaItem(new MediaFile(path, kind) { Version = FileVersion.Read(path) });
        // 导航的同步阶段会关闭旧查看器，所以先发起导航，再开始播放。
        var navigate = NavigateAsync(folder);
        ViewerState.SetPlaylist([item]);
        var play = ShowInViewerAsync(0, allowWhileScanning: true);
        await Task.WhenAll(play, navigate);
        // 已在该目录时不会重扫，直接把当前目录作为播放列表。
        if (_launchPath is not null && !GalleryState.IsScanning
            && string.Equals(GalleryState.CurrentFolder, NormalizeFolderPath(folder), StringComparison.OrdinalIgnoreCase))
            AdoptLaunchPlaylist();
    }

    /// <summary>目录扫描完成后用整个目录替换临时播放列表；当前项按路径匹配，不打断播放。</summary>
    private bool AdoptLaunchPlaylist()
    {
        if (_launchPath is not { } path) return false;
        _launchPath = null;
        // 扫描期间用户已经去了别的目录，就按普通导航处理。
        if (!string.Equals(GalleryState.CurrentFolder, NormalizeFolderPath(System.IO.Path.GetDirectoryName(path)!), StringComparison.OrdinalIgnoreCase))
            return false;
        var match = GalleryState.MediaItems.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        // 隐藏文件等不在列表中的情况保留单项播放列表。
        if (match is null) return true;
        ViewerState.SetPlaylist(GalleryState.MediaItems);
        SelectItem(match);
        return true;
    }

    /// <summary>瀑布流、网格和自适应视图里把某项滚到视口中间。</summary>
    private void BringItemIntoView(MediaItem item)
    {
        if (IsFilmstrip || _closed) return;
        int index = GalleryState.Items.IndexOf(item);
        if (index < 0 || Masonry.BoundsOf(index) is not { } bounds) return;
        double target = bounds.Y + bounds.Height / 2 - GalleryScroller.ViewportHeight / 2;
        if (bounds.Y < GalleryScroller.VerticalOffset || bounds.Bottom > GalleryScroller.VerticalOffset + GalleryScroller.ViewportHeight)
            GalleryScroller.ChangeView(null, Math.Max(0, target), null, true);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
