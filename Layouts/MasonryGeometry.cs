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
    public const double DetailsRowHeight = 60;
    public const double DetailsMinWidth = 820;
    private readonly double[] _maxBottoms;

    public static MasonryGeometry Details(int count, double width) => new(
        Enumerable.Range(0, count).Select(index => new TileBounds(0, index * DetailsRowHeight,
            Math.Max(DetailsMinWidth, double.IsFinite(width) ? width : DetailsMinWidth), DetailsRowHeight)).ToArray());

    private MasonryGeometry(TileBounds[] bounds)
    {
        Bounds = bounds;
        _maxBottoms = new double[bounds.Length];
        double maxBottom = 0;
        for (int i = 0; i < bounds.Length; i++) _maxBottoms[i] = maxBottom = Math.Max(maxBottom, bounds[i].Bottom);
        ExtentHeight = maxBottom;
    }

    /// <summary>
    /// 自适应行：每行图片等高并铺满整行宽度，保持各自比例；目标行高取缩略图大小。
    /// 断行时比较放入与不放入下一张哪种行高更接近目标，最后一行不放大。
    /// </summary>
    public static MasonryGeometry Justified(int count, double width, double targetHeight, double spacing, Func<int, double> ratioAt,
        double imageInset = 0, double captionHeight = 0)
    {
        width = double.IsFinite(width) ? Math.Max(0, width) : 240;
        targetHeight = double.IsFinite(targetHeight) ? Math.Max(1, targetHeight) : 240;
        spacing = double.IsFinite(spacing) ? Math.Max(0, spacing) : 6;
        imageInset = double.IsFinite(imageInset) ? Math.Max(0, imageInset) : 0;
        captionHeight = double.IsFinite(captionHeight) ? Math.Max(0, captionHeight) : 0;
        var bounds = new TileBounds[count];
        var ratios = new double[count];
        for (int i = 0; i < count; i++)
        {
            double ratio = ratioAt(i);
            ratios[i] = double.IsFinite(ratio) && ratio > 0 ? Math.Clamp(ratio, 0.25, 4) : 1;
        }

        // 行内图片部分可用的宽度；内边距和间距不随缩放变化。
        double ImageWidth(int start, int end) => width - spacing * (end - start - 1) - imageInset * 2 * (end - start);
        double RowHeight(int start, int end, double ratioSum) => Math.Max(1, ImageWidth(start, end)) / ratioSum;

        double y = 0;
        int rowStart = 0;
        while (rowStart < count)
        {
            int end = rowStart + 1;
            double ratioSum = ratios[rowStart];
            while (end < count && RowHeight(rowStart, end, ratioSum) > targetHeight)
            {
                double withNext = RowHeight(rowStart, end + 1, ratioSum + ratios[end]);
                double current = RowHeight(rowStart, end, ratioSum);
                if (Math.Abs(Math.Log(withNext / targetHeight)) > Math.Abs(Math.Log(current / targetHeight))) break;
                ratioSum += ratios[end++];
            }
            double height = RowHeight(rowStart, end, ratioSum);
            bool lastRow = end == count && height > targetHeight;
            if (lastRow) height = targetHeight;
            double x = 0;
            for (int i = rowStart; i < end; i++)
            {
                double tileWidth = height * ratios[i] + imageInset * 2;
                // 末项吸收浮点误差，整行右缘与容器对齐。
                if (!lastRow && i == end - 1) tileWidth = Math.Max(0, width - x);
                bounds[i] = new TileBounds(x, y, tileWidth, height + imageInset * 2 + captionHeight);
                x += tileWidth + spacing;
            }
            y += height + imageInset * 2 + captionHeight + spacing;
            rowStart = end;
        }
        return new MasonryGeometry(bounds);
    }

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
