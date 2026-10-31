namespace Fallery.Layouts;

public readonly record struct TileBounds(double X, double Y, double Width, double Height)
{
    public double Bottom => Y + Height;
}

// 纯几何缓存不依赖 WinRT；滚动时不会重新读取每个媒体项。
public sealed class MasonryGeometry
{
    public TileBounds[] Bounds { get; }
    public double ExtentHeight { get; }
    private readonly double[] _maxBottoms;

    public MasonryGeometry(int count, double width, double desiredWidth, double spacing, Func<int, double> ratioAt,
        double imageInset = 0, double captionHeight = 0)
    {
        width = double.IsFinite(width) ? Math.Max(0, width) : 240;
        desiredWidth = double.IsFinite(desiredWidth) ? Math.Max(1, desiredWidth) : 240;
        spacing = double.IsFinite(spacing) ? Math.Max(0, spacing) : 6;
        imageInset = double.IsFinite(imageInset) ? Math.Max(0, imageInset) : 0;
        captionHeight = double.IsFinite(captionHeight) ? Math.Max(0, captionHeight) : 0;
        int columns = Math.Max(1, (int)((width + spacing) / (desiredWidth + spacing)));
        double columnWidth = Math.Max(0, (width - spacing * (columns - 1)) / columns);
        var heights = new double[columns];
        Bounds = new TileBounds[count];
        _maxBottoms = new double[count];
        double maxBottom = 0;
        for (int i = 0; i < count; i++)
        {
            double ratio = ratioAt(i);
            ratio = double.IsFinite(ratio) && ratio > 0 ? Math.Clamp(ratio, 0.25, 4) : 1;
            int column = 0;
            for (int c = 1; c < columns; c++)
                if (heights[c] < heights[column]) column = c;
            // 文件名占独立一行，不能挤压预览图并改变原本的显示比例。
            double height = Math.Max(0, columnWidth - imageInset * 2) / ratio + imageInset * 2 + captionHeight;
            var bounds = new TileBounds(column * (columnWidth + spacing), heights[column], columnWidth, height);
            Bounds[i] = bounds;
            heights[column] = bounds.Bottom + spacing;
            _maxBottoms[i] = maxBottom = Math.Max(maxBottom, bounds.Bottom);
        }
        ExtentHeight = maxBottom;
    }

    public IEnumerable<int> GetVisibleIndices(double top, double height)
    {
        if (height <= 0 || double.IsNaN(top) || double.IsNaN(height)) yield break;
        double bottom = top + height;
        int low = 0, high = Bounds.Length;
        // 高度并不按索引递增，前缀最大下沿确保不会漏掉跨过视口顶部的长图。
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (_maxBottoms[mid] <= top) low = mid + 1;
            else high = mid;
        }
        for (int i = low; i < Bounds.Length && Bounds[i].Y < bottom; i++)
            if (Bounds[i].Bottom > top) yield return i;
    }
}
