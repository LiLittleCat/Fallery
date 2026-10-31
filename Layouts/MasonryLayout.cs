using System.Collections.Specialized;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Fallery.Layouts;

/// <summary>
/// 瀑布流虚拟化布局：每一项放进当前最短的那一列，只创建视口附近的元素。
/// 位置整体缓存，只有宽度、数量变化或调用 Refresh() 后才重算；滚动时只筛选可见区间。
/// </summary>
public sealed class MasonryLayout : VirtualizingLayout
{
    public static readonly DependencyProperty DesiredColumnWidthProperty = DependencyProperty.Register(
        nameof(DesiredColumnWidth), typeof(double), typeof(MasonryLayout),
        new PropertyMetadata(240.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(MasonryLayout),
        new PropertyMetadata(6.0, OnLayoutPropertyChanged));

    // 全景图、长截图夹紧到这个范围，避免出现极细或极长的格子
    private const double MinAspectRatio = 0.25;
    private const double MaxAspectRatio = 4.0;

    private int _version;

    public double DesiredColumnWidth
    {
        get => (double)GetValue(DesiredColumnWidthProperty);
        set => SetValue(DesiredColumnWidthProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>宽高比探测更新后调用，强制重算所有位置。</summary>
    public void Refresh()
    {
        _version++;
        InvalidateMeasure();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MasonryLayout)d).Refresh();

    private sealed class State
    {
        public Rect[] Bounds = [];
        public double ExtentHeight;
        public double Width = double.NaN;
        public int Version = -1;
        public HashSet<int> Realized = [];
    }

    protected override void InitializeForContextCore(VirtualizingLayoutContext context)
        => context.LayoutState = new State();

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context)
        => context.LayoutState = null;

    protected override void OnItemsChangedCore(VirtualizingLayoutContext context, object source, NotifyCollectionChangedEventArgs args)
    {
        // 索引已失效，旧元素由 ItemsRepeater 自己清理
        var state = (State)context.LayoutState;
        state.Realized.Clear();
        state.Version = -1;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var state = (State)context.LayoutState;
        double width = double.IsInfinity(availableSize.Width) ? DesiredColumnWidth : availableSize.Width;

        if (state.Version != _version || state.Width != width || state.Bounds.Length != context.ItemCount)
        {
            ComputeBounds(context, state, width);
        }

        var viewport = context.RealizationRect;
        var visible = new HashSet<int>();
        for (int i = 0; i < state.Bounds.Length; i++)
        {
            var b = state.Bounds[i];
            if (b.Y < viewport.Y + viewport.Height && b.Y + b.Height > viewport.Y) visible.Add(i);
        }

        // 先回收滚出去的，ItemsRepeater 才能把它们复用给新进来的
        foreach (int i in state.Realized)
        {
            if (!visible.Contains(i)) context.RecycleElement(context.GetOrCreateElementAt(i));
        }

        foreach (int i in visible)
        {
            var b = state.Bounds[i];
            context.GetOrCreateElementAt(i).Measure(new Size(b.Width, b.Height));
        }

        state.Realized = visible;
        return new Size(width, state.ExtentHeight);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        var state = (State)context.LayoutState;
        foreach (int i in state.Realized)
        {
            context.GetOrCreateElementAt(i).Arrange(state.Bounds[i]);
        }
        return finalSize;
    }

    private void ComputeBounds(VirtualizingLayoutContext context, State state, double width)
    {
        int count = context.ItemCount;
        double spacing = Spacing;
        int columns = Math.Max(1, (int)((width + spacing) / (DesiredColumnWidth + spacing)));
        double columnWidth = (width - spacing * (columns - 1)) / columns;
        var columnHeights = new double[columns];

        var bounds = new Rect[count];
        for (int i = 0; i < count; i++)
        {
            double ratio = (context.GetItemAt(i) as MediaItem)?.AspectRatio ?? 1.0;
            ratio = Math.Clamp(ratio, MinAspectRatio, MaxAspectRatio);

            int column = 0;
            for (int c = 1; c < columns; c++)
            {
                if (columnHeights[c] < columnHeights[column]) column = c;
            }

            double height = columnWidth / ratio;
            bounds[i] = new Rect(column * (columnWidth + spacing), columnHeights[column], columnWidth, height);
            columnHeights[column] += height + spacing;
        }

        state.Bounds = bounds;
        state.ExtentHeight = count == 0 ? 0 : Math.Max(0, columnHeights.Max() - spacing);
        state.Width = width;
        state.Version = _version;
    }
}
