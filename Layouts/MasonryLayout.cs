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
        public MasonryGeometry Geometry = new(0, 0, 240, 6, _ => 1);
        public double Width = double.NaN;
        public int Version = -1;
        public Dictionary<int, UIElement> Realized = [];
    }

    protected override void InitializeForContextCore(VirtualizingLayoutContext context)
        => context.LayoutState = new State();

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context)
    {
        if (context.LayoutState is State state) RecycleAll(context, state);
        context.LayoutState = null;
    }

    protected override void OnItemsChangedCore(VirtualizingLayoutContext context, object source, NotifyCollectionChangedEventArgs args)
    {
        // 更换 ItemsSource 不保证自动回收同索引元素；否则清空后的旧卡片会跳过 ElementPrepared。
        var state = (State)context.LayoutState;
        RecycleAll(context, state);
        state.Version = -1;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var state = (State)context.LayoutState;
        double width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : DesiredColumnWidth;

        if (state.Version != _version || state.Width != width || state.Geometry.Bounds.Length != context.ItemCount)
        {
            ComputeBounds(context, state, width);
        }

        var viewport = context.RealizationRect;
        var visible = state.Geometry.GetVisibleIndices(viewport.Y, viewport.Height).ToHashSet();

        // 先回收滚出去的，ItemsRepeater 才能把它们复用给新进来的
        foreach (int i in state.Realized.Keys.Where(i => !visible.Contains(i)).ToArray())
        {
            context.RecycleElement(state.Realized[i]);
            state.Realized.Remove(i);
        }

        foreach (int i in visible)
        {
            var b = state.Geometry.Bounds[i];
            // 布局显式管理生命周期，避免框架自动回收与本地索引集合各自保留不同状态。
            if (!state.Realized.TryGetValue(i, out var element))
                state.Realized.Add(i, element = context.GetOrCreateElementAt(i, ElementRealizationOptions.SuppressAutoRecycle));
            element.Measure(new Size(b.Width, b.Height));
        }

        return new Size(width, state.Geometry.ExtentHeight);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        var state = (State)context.LayoutState;
        foreach (var (i, element) in state.Realized)
        {
            var b = state.Geometry.Bounds[i];
            element.Arrange(new Rect(b.X, b.Y, b.Width, b.Height));
        }
        return finalSize;
    }

    private static void RecycleAll(VirtualizingLayoutContext context, State state)
    {
        foreach (var element in state.Realized.Values) context.RecycleElement(element);
        state.Realized.Clear();
    }

    private void ComputeBounds(VirtualizingLayoutContext context, State state, double width)
    {
        state.Geometry = new MasonryGeometry(context.ItemCount, width, DesiredColumnWidth, Spacing,
            i => (context.GetItemAt(i) as MediaItem)?.AspectRatio ?? 1.0, imageInset: 6, captionHeight: 22);
        state.Width = width;
        state.Version = _version;
    }
}
