using System.Collections.Specialized;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Fallery.Layouts;

/// <summary>
/// 瀑布流 / 网格 / 自适应行共用的虚拟化布局，只创建视口附近的元素。瀑布流把每一项放进当前最短的那一列。
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
    private MasonryGeometry? _lastGeometry;

    /// <summary>最近一次布局中某项的位置，用于把选中项滚到视口内；滚动路径不调用。</summary>
    public TileBounds? BoundsOf(int index) =>
        _lastGeometry is { } geometry && index >= 0 && index < geometry.Bounds.Length ? geometry.Bounds[index] : null;
    public IEnumerable<int> IntersectingIndices(double x, double y, double width, double height)
    {
        if (_lastGeometry is not { } geometry || width <= 0 || height <= 0) yield break;
        foreach (int index in geometry.GetVisibleIndices(y, height))
        {
            var bounds = geometry.Bounds[index];
            if (bounds.X < x + width && bounds.X + bounds.Width > x) yield return index;
        }
    }
    public bool UniformTiles { get; set; }
    public bool JustifiedRows { get; set; }
    public bool ShowFileNames { get; set; } = true;
    public bool ShowCards { get; set; } = true;

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
        // 文件夹封面是固定横向构图，方形占位会在叠图和名称之间留下大块空白。
        Func<int, double> ratioAt = i => context.GetItemAt(i) is MediaItem item
            ? item.IsFolder ? 1.25 : UniformTiles ? 1.0 : item.AspectRatio
            : 1.0;
        double caption = ShowFileNames ? 22 : 0;
        // 卡片内边距 5 + 描边 1；不显示卡片背景时图片贴满格子。
        double inset = ShowCards ? 6 : 0;
        // 自适应行把缩略图大小当作目标行高，按列宽的约四分之三取值，与其他视图的单张观感接近。
        state.Geometry = JustifiedRows
            ? MasonryGeometry.Justified(context.ItemCount, width, DesiredColumnWidth * 0.75, Spacing, ratioAt, imageInset: inset, captionHeight: caption)
            : new MasonryGeometry(context.ItemCount, width, DesiredColumnWidth, Spacing, ratioAt, imageInset: inset, captionHeight: caption);
        state.Width = width;
        state.Version = _version;
        _lastGeometry = state.Geometry;
    }
}
