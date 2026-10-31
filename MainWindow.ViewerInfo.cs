using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Fallery;

public sealed partial class MainWindow
{
    private const double DetailsWidth = 300;
    private static readonly int[] ZoomPresets = [25, 50, 100, 200, 400];
    private CancellationTokenSource? _detailsCts;
    private MediaDetails? _viewerDetails;
    private bool _detailsOpen;
    private bool _updatingZoomSlider;

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
        finally { if (ReferenceEquals(_detailsCts, cts)) _detailsCts = null; }
    }

    private void ShowViewerDetails(MediaDetails details)
    {
        _viewerDetails = details;
        ViewerFormatText.Text = details.Format;
        ViewerResolutionText.Text = details.Resolution;
        ViewerResolutionIcon.Visibility = ViewerResolutionText.Visibility = details.Resolution.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewerSizeText.Text = details.Size;
        UpdateZoomIndicators();

        DetailsContent.Children.Clear();
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        foreach (var (group, rows) in details.Groups)
        {
            if (rows.Count == 0) continue;
            var section = new StackPanel { Spacing = 8 };
            section.Children.Add(new TextBlock { Text = group, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = secondary });
            foreach (var (label, value) in rows)
            {
                var row = new StackPanel { Spacing = 1 };
                row.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = secondary });
                row.Children.Add(new TextBlock { Text = value, FontSize = 13, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
                section.Children.Add(row);
            }
            DetailsContent.Children.Add(section);
        }
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e) => SetDetailsOpen(!_detailsOpen);

    private void SetDetailsOpen(bool open)
    {
        _detailsOpen = open;
        ApplyDetailsLayout();
        QueueSettingsSave();
    }

    private void ApplyDetailsLayout()
    {
        DetailsColumn.Width = new GridLength(_detailsOpen ? DetailsWidth : 0);
        DetailsPanel.Visibility = _detailsOpen ? Visibility.Visible : Visibility.Collapsed;
        DetailsToggle.IsChecked = _detailsOpen;
    }

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
        if (width == 0 || height == 0 || viewportWidth <= 0 || viewportHeight <= 0) return 0;
        double fitWidth = Math.Min(viewportWidth, viewportHeight * width / height);
        return fitWidth * (ImageScroller.XamlRoot?.RasterizationScale ?? 1) / width;
    }

    private void ZoomTo(double factor)
    {
        factor = Math.Clamp(factor, ImageScroller.MinZoomFactor, ImageScroller.MaxZoomFactor);
        double old = ImageScroller.ZoomFactor;
        // 以视口中心为锚点缩放，避免画面跳到左上角。
        double centerX = (ImageScroller.HorizontalOffset + ImageScroller.ViewportWidth / 2) / old;
        double centerY = (ImageScroller.VerticalOffset + ImageScroller.ViewportHeight / 2) / old;
        ImageScroller.ChangeView(Math.Max(0, centerX * factor - ImageScroller.ViewportWidth / 2),
            Math.Max(0, centerY * factor - ImageScroller.ViewportHeight / 2), (float)factor, disableAnimation: true);
    }

    private void ZoomToPercent(double percent)
    {
        double fit = FitScale();
        if (fit > 0) ZoomTo(percent / 100 / fit);
    }

    private void ZoomBy(double ratio) => ZoomTo(ImageScroller.ZoomFactor * ratio);

    private void UpdateZoomIndicators()
    {
        double fit = FitScale();
        ZoomPercentText.Text = fit > 0 ? $"{Math.Round(ImageScroller.ZoomFactor * fit * 100)}%" : "适应";
        _updatingZoomSlider = true;
        ZoomSlider.Value = Math.Log2(ImageScroller.ZoomFactor);
        _updatingZoomSlider = false;
    }

    private void ImageScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateZoomIndicators();
    private void ZoomFit_Click(object sender, RoutedEventArgs e) => FitImageToViewport();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomBy(1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(1 / 1.25);

    private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingZoomSlider) ZoomTo(Math.Pow(2, e.NewValue));
    }
}
