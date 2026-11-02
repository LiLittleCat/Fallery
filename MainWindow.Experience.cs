using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Fallery;

public sealed partial class MainWindow
{
    private readonly AppSettingsStore _settingsStore = AppSettingsStore.Shared;
    private readonly DispatcherTimer _settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _settingsReady;
    private bool _closing;
    private bool _allowClose;
    private int _normalWidth = 1400;
    private int _normalHeight = 900;
    private bool _videoWindowDefaultsApplied;
    private bool _maximized;
    private string? _lastFolderToRestore;
    private int _navigationVersion;
    private Pointer? _gesturePointer;
    private Point _gestureOrigin;
    private string _statusBeforeGesture = "";
    private bool _sidebarVisible = true;
    private double _sidebarWidth = 260;

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_sidebarVisible)
        {
            _sidebarWidth = Math.Clamp(SidebarColumn.ActualWidth, 160, 480);
            _sidebarVisible = false;
            SidebarPanel.Visibility = SidebarSplitter.Visibility = Visibility.Collapsed;
            SidebarColumn.MinWidth = 0;
            SidebarColumn.Width = new GridLength(0);
        }
        else
        {
            _sidebarVisible = true;
            SidebarColumn.MinWidth = 160;
            SidebarColumn.Width = new GridLength(_sidebarWidth);
            SidebarPanel.Visibility = SidebarSplitter.Visibility = Visibility.Visible;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (GalleryState.CurrentFolder is { } folder) await NavigateAsync(folder, refresh: true);
    }

    private void InitializeExperience()
    {
        InitializeMarqueeSelection();
        AppWindow.Changed += AppWindow_Changed;
        AppWindow.Closing += AppWindow_Closing;
        _settingsTimer.Tick += SettingsTimer_Tick;
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Root_MouseNavigationPressed), true);
        ViewerStage.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(ViewerStage_PointerWheelChanged), true);
        ImageScroller.Loaded += ImageScroller_WheelLoaded;
        GallerySurface.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Gallery_PointerPressed), true);
        GallerySurface.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Gallery_PointerMoved), true);
        GallerySurface.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Gallery_PointerReleased), true);
        GallerySurface.PointerCanceled += Gallery_PointerCanceled;
        GallerySurface.PointerCaptureLost += Gallery_PointerCanceled;
        GallerySurface.RightTapped += (_, args) => args.Handled = true;
        GallerySurface.Tapped += GalleryBlank_Tapped;
    }

    private async Task RestoreSettingsAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        if (_closed || _closing) return;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        _normalWidth = Math.Min(settings.Width, workArea.Width);
        _normalHeight = Math.Min(settings.Height, workArea.Height);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(_normalWidth, _normalHeight));
        _videoWindowDefaultsApplied = settings.VideoWindowDefaultsApplied;
        _maximized = settings.Maximized && _videoWindowDefaultsApplied;
        if (_maximized && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        ApplyTheme(settings.Theme);
        ApplyBackgroundOpacity(settings.BackgroundOpacity);
        ThumbSize.Value = settings.ThumbnailSize;
        SidebarColumn.Width = new GridLength(settings.SidebarWidth);
        _sidebarWidth = settings.SidebarWidth;
        _treeSortField = settings.TreeSortField;
        _treeDescending = settings.TreeDescending;
        _treeShowModifiedTime = settings.TreeShowModifiedTime;
        _treeTimeField = settings.TreeTimeField;
        _treeExcludedFolders.Clear();
        _treeExcludedFolders.AddRange(settings.TreeExcludedFolders);
        UpdateTreeToolbar();
        GalleryState.SortField = settings.SortField;
        GalleryState.Descending = settings.Descending;
        GalleryState.ShowFileNames = settings.ShowFileNames;
        GalleryState.ShowCards = settings.ShowCards;
        GalleryState.DisplayMode = settings.DisplayMode;
        GalleryState.Filter = settings.Filter;
        GalleryState.ShowSubfolders = settings.ShowSubfolders;
        GalleryState.ShowHidden = settings.ShowHidden;
        _volume = settings.Volume;
        _muted = settings.Muted;
        VideoVolume.Value = _volume;
        SetPlaylistThumbnails(settings.PlaylistShowThumbnails);
        ResetViewerSidePanel();
        GalleryState.RestoreRecentFolders(settings.RecentFolders);
        RestoreFavoriteFolders(settings.FavoriteFolders);
        ApplyDisplayOptions();
        UpdateBrowseMenus();
        _lastFolderToRestore = settings.LastFolder;
        await RefreshTreePresentationAsync();
        if (!_videoWindowDefaultsApplied) _videoWindowDefaultsApplied = ApplyDefaultVideoWindowSize();
        _settingsReady = true;
        if (_videoWindowDefaultsApplied && !settings.VideoWindowDefaultsApplied) QueueSettingsSave();
    }

    private AppSettings CaptureSettings() => new()
    {
        Width = _browseWindowPlacement is { } placement ? placement.NormalPosition.Right - placement.NormalPosition.Left : _normalWidth,
        Height = _browseWindowPlacement is { } bounds ? bounds.NormalPosition.Bottom - bounds.NormalPosition.Top : _normalHeight,
        Maximized = _browseWindowPlacement is { } saved ? saved.ShowCommand == 3 : _maximized,
        VideoWindowDefaultsApplied = _videoWindowDefaultsApplied,
        LastFolder = GalleryState.CurrentFolder ?? _lastFolderToRestore,
        Theme = _themeIndex,
        BackgroundOpacity = _backgroundOpacity,
        ThumbnailSize = ThumbSize.Value,
        SidebarWidth = _sidebarVisible ? SidebarColumn.ActualWidth : _sidebarWidth,
        TreeSortField = _treeSortField,
        TreeDescending = _treeDescending,
        TreeShowModifiedTime = _treeShowModifiedTime,
        TreeTimeField = _treeTimeField,
        TreeExcludedFolders = [.. _treeExcludedFolders],
        SortField = GalleryState.SortField,
        Descending = GalleryState.Descending,
        DisplayMode = GalleryState.DisplayMode,
        ShowFileNames = GalleryState.ShowFileNames,
        ShowCards = GalleryState.ShowCards,
        Filter = GalleryState.Filter,
        ShowSubfolders = GalleryState.ShowSubfolders,
        ShowHidden = GalleryState.ShowHidden,
        Volume = _volume,
        Muted = _muted,
        PlaylistOpen = false,
        PlaylistShowThumbnails = _playlistShowThumbnails,
        DetailsOpen = false,
        RecentFolders = [.. GalleryState.RecentFolders],
        FavoriteFolders = [.. _favoriteFolders],
    };

    private void QueueSettingsSave()
    {
        if (!_settingsReady || _closed || _closing) return;
        _settingsTimer.Stop();
        _settingsTimer.Start();
    }

    private async void SettingsTimer_Tick(object? sender, object e)
    {
        _settingsTimer.Stop();
        if (_closed || _closing) return;
        if (!await _settingsStore.SaveAsync(CaptureSettings()) && !_closed && !_closing)
            GalleryState.Status = "无法保存设置，请检查用户目录的写入权限。";
    }

    private void FolderTree_SizeChanged(object sender, SizeChangedEventArgs e) => QueueSettingsSave();

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidSizeChange) ApplyViewerSidePanel();
        if (!_settingsReady || sender.Presenter is not OverlappedPresenter presenter
            || presenter.State == OverlappedPresenterState.Minimized) return;
        _maximized = presenter.State == OverlappedPresenterState.Maximized;
        if (!_maximized)
        {
            _normalWidth = sender.Size.Width;
            _normalHeight = sender.Size.Height;
        }
        QueueSettingsSave();
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        StopHoverPreview();
        _settingsDialog?.Hide();
        _settingsTimer.Stop();
        GalleryState.Cancel();
        CloseViewer();
        // 退出前等最后一次写入完成，避免异步保存被进程结束打断。
        await _resumeSaveTask;
        if (_settingsReady) await _settingsStore.SaveAsync(CaptureSettings());
        _allowClose = true;
        DispatcherQueue.TryEnqueue(Close);
    }

    private async void SortField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string field } && Enum.TryParse<MediaSortField>(field, out var value))
        {
            GalleryState.SortField = value;
            UpdateBrowseMenus();
            QueueSettingsSave();
            await ApplySortAsync();
        }
    }

    private async void SortDirection_Click(object sender, RoutedEventArgs e)
    {
        GalleryState.Descending = (sender as RadioMenuFlyoutItem)?.Tag as string == "Descending";
        UpdateBrowseMenus();
        QueueSettingsSave();
        await ApplySortAsync();
    }

    private Task ApplySortAsync() => GalleryState.IsScanning ? ReloadBrowseOptionsAsync() : GalleryState.ApplySortAsync();

    private async Task GoAdjacentFolderAsync(bool forward)
    {
        if (_closed || _closing) return;
        var target = await GalleryState.FindAdjacentAsync(forward);
        if (!_closed && !_closing && target is not null) await NavigateAsync(target);
    }

    private void Gallery_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _suppressMarqueeTap = false;
        if (_closed || _closing || GalleryState.IsScanning || (Viewer.Visibility == Visibility.Visible && !IsFilmstrip)
            || e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        if (TryStartMarquee(e)) return;
        var point = e.GetCurrentPoint(GallerySurface);
        if (!point.Properties.IsRightButtonPressed || point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed) return;
        for (var source = e.OriginalSource as DependencyObject; source is not null;
             source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source))
            if (source is ScrollBar || source == Viewer) return;
        if (!GallerySurface.CapturePointer(e.Pointer)) return;
        _gesturePointer = e.Pointer;
        _gestureItem = FindMediaItem(e.OriginalSource as DependencyObject);
        _gestureOrigin = point.Position;
        _statusBeforeGesture = GalleryState.Status;
        e.Handled = true;
    }

    private NavigationAction GetGesture(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(GallerySurface).Position;
        return NavigationGesture.Classify(point.X - _gestureOrigin.X, point.Y - _gestureOrigin.Y);
    }

    private void Gallery_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_marqueePointer?.PointerId == e.Pointer.PointerId) { MoveMarquee(e); return; }
        if (_gesturePointer?.PointerId != e.Pointer.PointerId) return;
        string? label = GetGesture(e) switch
        {
            NavigationAction.Parent => "上级目录",
            NavigationAction.Back => "后退",
            NavigationAction.Previous => "上一个媒体目录",
            NavigationAction.Next => "下一个媒体目录",
            _ => null,
        };
        GalleryState.Status = label is null ? _statusBeforeGesture : $"手势：{label}（松开执行，Esc 取消）";
        e.Handled = true;
    }

    private async void Gallery_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_marqueePointer?.PointerId == e.Pointer.PointerId)
        {
            if (e.GetCurrentPoint(GalleryScroller).Properties.IsLeftButtonPressed) return;
            _marqueeViewportPoint = e.GetCurrentPoint(GalleryScroller).Position;
            UpdateMarqueeFromViewport();
            EndMarqueeSelection(cancel: false);
            e.Handled = true;
            return;
        }
        if (_gesturePointer?.PointerId != e.Pointer.PointerId) return;
        if (e.GetCurrentPoint(GallerySurface).Properties.IsRightButtonPressed) return;
        var action = GetGesture(e);
        var item = _gestureItem;
        var origin = _gestureOrigin;
        var end = e.GetCurrentPoint(GallerySurface).Position;
        bool click = Math.Abs(end.X - origin.X) < 8 && Math.Abs(end.Y - origin.Y) < 8;
        _lastRightRelease = Environment.TickCount64;
        CancelGesture();
        e.Handled = true;
        switch (action)
        {
            case NavigationAction.Parent: await GoUpAsync(); break;
            case NavigationAction.Back: await GoBackAsync(); break;
            case NavigationAction.Previous: await GoAdjacentFolderAsync(false); break;
            case NavigationAction.Next: await GoAdjacentFolderAsync(true); break;
            case NavigationAction.None when click:
                if (item is not null) ShowItemMenu(GallerySurface, origin, item.Path, item.IsFolder);
                else if (GalleryState.CurrentFolder is { } path) ShowItemMenu(GallerySurface, origin, path, true, allowDelete: false);
                break;
        }
    }

    private void Gallery_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_marqueePointer?.PointerId == e.Pointer.PointerId) EndMarqueeSelection(cancel: true);
        if (_gesturePointer?.PointerId == e.Pointer.PointerId) CancelGesture();
    }

    private void CancelGesture()
    {
        var pointer = _gesturePointer;
        _gesturePointer = null;
        _gestureItem = null;
        if (pointer is null) return;
        GalleryState.Status = _statusBeforeGesture;
        GallerySurface.ReleasePointerCapture(pointer);
    }
}
