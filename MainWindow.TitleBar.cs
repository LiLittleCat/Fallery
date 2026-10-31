using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Fallery;

public sealed partial class MainWindow
{
    private void TitleBar_LayoutUpdated(object? sender, object e)
    {
        if (_closed || _closing || !ExtendsContentIntoTitleBar
            || AppTitleBar.Visibility != Visibility.Visible || AppTitleBar.XamlRoot is null) return;

        // SDK 1.8 把整个 TitleBar.Content 排除在拖动区域外；查看器里只有返回按钮需要接收点击。
        FrameworkElement[] controls = ViewerState.IsOpen && !IsFilmstrip
            ? [CloseViewerButton]
            : [GalleryToolbar, SettingsButton];
        double scale = AppTitleBar.XamlRoot.RasterizationScale;
        var regions = new List<RectInt32>(controls.Length);
        foreach (var control in controls)
        {
            if (control.Visibility != Visibility.Visible || control.ActualWidth <= 0 || control.ActualHeight <= 0) continue;
            var bounds = control.TransformToVisual(Root).TransformBounds(
                new Windows.Foundation.Rect(0, 0, control.ActualWidth, control.ActualHeight));
            int left = (int)Math.Floor(bounds.Left * scale);
            int top = (int)Math.Floor(bounds.Top * scale);
            regions.Add(new RectInt32(left, top,
                (int)Math.Ceiling(bounds.Right * scale) - left,
                (int)Math.Ceiling(bounds.Bottom * scale) - top));
        }

        // 布局完成后再读坐标，兼容切换查看器、缩放和跨屏 DPI；区域没变时不重复写入。
        var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        var current = source.GetRegionRects(NonClientRegionKind.Passthrough);
        if (current.Length == regions.Count && current.Select((rect, index) =>
            rect.X == regions[index].X && rect.Y == regions[index].Y
            && rect.Width == regions[index].Width && rect.Height == regions[index].Height).All(equal => equal)) return;
        source.SetRegionRects(NonClientRegionKind.Passthrough, regions.ToArray());
    }
}
