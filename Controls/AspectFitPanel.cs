using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Fallery.Controls;

// 让原生视频表面只占实际画面，补边由窗口磨砂背景显示，而不是播放器绘制黑色。
public sealed class AspectFitPanel : Panel
{
    public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(
        nameof(AspectRatio), typeof(double), typeof(AspectFitPanel),
        new PropertyMetadata(1d, (sender, _) => ((AspectFitPanel)sender).InvalidateMeasure()));

    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, double.IsFinite(value) && value > 0 ? value : 1d);
    }

    private Size Fit(Size bounds)
    {
        double width = Math.Min(bounds.Width, bounds.Height * AspectRatio);
        return new Size(width, width / AspectRatio);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsFinite(availableSize.Width) || !double.IsFinite(availableSize.Height)) return new Size();
        if (Children.Count > 0) Children[0].Measure(Fit(availableSize));
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count > 0)
        {
            var size = Fit(finalSize);
            Children[0].Arrange(new Rect((finalSize.Width - size.Width) / 2,
                (finalSize.Height - size.Height) / 2, size.Width, size.Height));
        }
        return finalSize;
    }
}
