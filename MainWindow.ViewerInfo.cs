using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;

namespace Fallery;

public sealed partial class MainWindow
{
    private static readonly int[] ZoomPresets = [25, 50, 100, 200, 400];
    private CancellationTokenSource? _detailsCts;
    private MediaDetails? _viewerDetails;
    private bool _detailsOpen;
    private bool _updatingZoomSlider;
    private bool _imageFitMode = true;
    private bool _imageFitPending;

    private void InitializeViewerInfo()
    {
        var fit = new MenuFlyoutItem { Text = "适应窗口" };
        fit.Click += (_, _) => FitImageToViewport();
        ZoomMenu.Items.Add(fit);
        ZoomMenu.Items.Add(new MenuFlyoutSeparator());
        foreach (int percent in ZoomPresets)
        {
            var entry = new MenuFlyoutItem { Text = percent == 100 ? "100%（实际大小）" : $"{percent}%" };
            entry.Click += (_, _) => ZoomToPercent(percent);
            ZoomMenu.Items.Add(entry);
        }
    }

    /// <summary>底栏先显示不读文件内容的基础信息，分辨率和拍摄参数在后台补齐。</summary>
    private async Task LoadViewerDetailsAsync(MediaItem item)
    {
        _detailsCts?.Cancel();
        using var cts = new CancellationTokenSource();
        _detailsCts = cts;
        ShowViewerDetails(MediaDetailsReader.Basic(item.Path, item.Kind));
        try
        {
            var details = await MediaDetailsReader.ReadAsync(item.Path, item.Kind, cts.Token);
            if (!cts.IsCancellationRequested && !_closed && ReferenceEquals(ViewerState.CurrentItem, item)) ShowViewerDetails(details);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_detailsCts, cts))
            {
                _detailsCts = null;
                if (!_closed) UpdateZoomIndicators();
            }
        }
    }

    private void ShowViewerDetails(MediaDetails details)
    {
        _viewerDetails = details;
        ViewerFormatText.Text = details.Format;
        ViewerResolutionText.Text = details.Resolution;
        ViewerResolutionIcon.Visibility = ViewerResolutionText.Visibility = details.Resolution.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewerSizeText.Text = details.Size;
        UpdateZoomIndicators();

        RenderViewerDetails(details);
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e) => SetDetailsOpen(!_detailsOpen);

    private void SetDetailsOpen(bool open)
    {
        _detailsOpen = open;
        if (open) _playlistOpen = false;
        ApplyViewerSidePanel();
    }

    private void ApplyDetailsLayout() => ApplyViewerSidePanel();

    private void DetailsCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (ViewerState.CurrentItem is not { } item) return;
        var data = new DataPackage();
        data.SetText(item.Path);
        Clipboard.SetContent(data);
        Clipboard.Flush();
        GalleryState.Status = "已复制完整路径";
    }

    private async void DetailsReveal_Click(object sender, RoutedEventArgs e)
    {
        if (ViewerState.CurrentItem is not { } item) return;
        try { await RevealPathAsync(item.Path, false); }
        catch (Exception ex) { if (!_closed) GalleryState.Status = $"操作失败：{ex.Message}"; }
    }

    // ───────────── 缩放 ─────────────

    /// <summary>缩放倍数为 1 时图片适应窗口；百分比按原图像素换算，100% 为实际大小。</summary>
    private double FitScale()
    {
        uint width = _viewerDetails?.Width ?? 0, height = _viewerDetails?.Height ?? 0;
        if ((width == 0 || height == 0) && ViewerImage.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap)
        {
            width = (uint)bitmap.PixelWidth;
            height = (uint)bitmap.PixelHeight;
        }
        double viewportWidth = ImageScroller.ViewportWidth, viewportHeight = ImageScroller.ViewportHeight;
        if (_viewerQuarterTurns % 2 != 0) (width, height) = (height, width);
        if (width == 0 || height == 0 || viewportWidth <= 0 || viewportHeight <= 0) return 0;
        double fitWidth = Math.Min(viewportWidth, viewportHeight * width / height);
        return fitWidth * (ImageScroller.XamlRoot?.RasterizationScale ?? 1) / width;
    }

    private void ZoomTo(double factor)
    {
        StopMotionPlayback();
        _imageFitMode = false;
        _imageFitPending = false;
        factor = Math.Clamp(factor, ImageScroller.MinZoomFactor, ImageScroller.MaxZoomFactor);
        double old = ImageScroller.ZoomFactor;
        // 以视口中心为锚点缩放，避免画面跳到左上角。
        double centerX = (ImageScroller.HorizontalOffset + ImageScroller.ViewportWidth / 2) / old;
        double centerY = (ImageScroller.VerticalOffset + ImageScroller.ViewportHeight / 2) / old;
        if (!ImageScroller.ChangeView(Math.Max(0, centerX * factor - ImageScroller.ViewportWidth / 2),
            Math.Max(0, centerY * factor - ImageScroller.ViewportHeight / 2), (float)factor, disableAnimation: true))
            UpdateZoomIndicators();
    }

    private void ZoomToPercent(double percent)
    {
        double fit = FitScale();
        if (fit > 0) ZoomTo(percent / 100 / fit);
    }

    private void ZoomBy(double ratio) => ZoomTo(ImageScroller.ZoomFactor * ratio);

    private void UpdateZoomIndicators()
    {
        // 等原图尺寸就绪再显示比例，避免先按解码预览尺寸算一次、随后跳成另一比例。
        bool dimensionsReady = _viewerDetails is { Width: > 0, Height: > 0 } || _detailsCts is null;
        double fit = ViewerImage.Source is not null && dimensionsReady ? FitScale() : 0;
        double factor = _imageFitPending ? 1 : ImageScroller.ZoomFactor;
        string percent = fit > 0 ? $"{Math.Round(factor * fit * 100)}%" : "—";
        ZoomPercentText.Text = _imageFitMode ? $"适应窗口 · {percent}" : percent;
        _updatingZoomSlider = true;
        ZoomSlider.Value = Math.Log2(factor);
        _updatingZoomSlider = false;
    }

    private void ImageScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        bool atFit = Math.Abs(ImageScroller.ZoomFactor - 1) <= 0.0001;
        if (_imageFitPending)
        {
            if (atFit) _imageFitPending = false;
        }
        else if (!atFit) _imageFitMode = false; // Ctrl＋滚轮和触控缩放也切换为百分比显示。
        UpdateZoomIndicators();
    }
    private void ZoomFit_Click(object sender, RoutedEventArgs e) => FitImageToViewport();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomBy(1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(1 / 1.25);

    private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingZoomSlider) ZoomTo(Math.Pow(2, e.NewValue));
    }
}
