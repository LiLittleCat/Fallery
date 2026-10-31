using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Fallery.Controls;

// 旋转同时参与测量，ScrollViewer 才能按旋转后的宽高缩放和滚动，GIF 仍使用原图源。
public sealed class QuarterTurnPanel : Panel
{
    public static readonly DependencyProperty QuarterTurnsProperty = DependencyProperty.Register(
        nameof(QuarterTurns), typeof(int), typeof(QuarterTurnPanel),
        new PropertyMetadata(0, (sender, _) => ((QuarterTurnPanel)sender).InvalidateMeasure()));

    public int QuarterTurns
    {
        get => (int)GetValue(QuarterTurnsProperty);
        set => SetValue(QuarterTurnsProperty, (value % 4 + 4) % 4);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return new Size();
        bool swap = QuarterTurns % 2 != 0;
        var child = Children[0];
        child.Measure(swap ? new Size(availableSize.Height, availableSize.Width) : availableSize);
        return swap ? new Size(child.DesiredSize.Height, child.DesiredSize.Width) : child.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0) return finalSize;
        bool swap = QuarterTurns % 2 != 0;
        double width = swap ? finalSize.Height : finalSize.Width;
        double height = swap ? finalSize.Width : finalSize.Height;
        var child = Children[0];
        child.RenderTransformOrigin = new Point(0, 0);
        child.RenderTransform = new MatrixTransform
        {
            Matrix = QuarterTurns switch
            {
                1 => new Matrix(0, 1, -1, 0, height, 0),
                2 => new Matrix(-1, 0, 0, -1, width, height),
                3 => new Matrix(0, -1, 1, 0, 0, width),
                _ => Matrix.Identity,
            },
        };
        child.Arrange(new Rect(0, 0, width, height));
        return finalSize;
    }
}
