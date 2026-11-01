using System.Reflection;
using Fallery.Layouts;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunMarqueeChecksAsync(MainWindow window)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var root = (FrameworkElement)window.Content;
        var scroll = (ScrollViewer)root.FindName("GalleryScroller");
        var overlay = (Canvas)root.FindName("MarqueeOverlay");
        var box = (Border)root.FindName("SelectionMarquee");
        var layout = (MasonryLayout)root.FindName("Masonry");
        var paths = (HashSet<string>)typeof(MainWindow).GetField("_selectedPaths", flags)!.GetValue(window)!;
        var originalMode = window.GalleryState.DisplayMode;
        async Task Settle() { root.UpdateLayout(); await Task.Delay(80); root.UpdateLayout(); }
        try
        {
            foreach (var mode in new[] { GalleryDisplayMode.Masonry, GalleryDisplayMode.Justified, GalleryDisplayMode.LargeIcons })
            {
                await (Task)Call("SetDisplayModeAsync", mode)!;
                await Settle();
                var items = window.GalleryState.Items;
                var bounds = Enumerable.Range(0, items.Count).Select(index => layout.BoundsOf(index)!.Value).ToArray();
                var end = new Point(bounds.Max(b => b.X + b.Width) / 2, bounds[8].Bottom - 1);
                var start = new Point(-1, -1);
                var hits = Enumerable.Range(0, bounds.Length).Where(index => bounds[index].X < end.X && bounds[index].Y < end.Y).ToArray();
                var expected = Enumerable.Range(hits[0], hits[^1] - hits[0] + 1)
                    .Select(index => items[index].Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Call("SelectItem", items[^1]);
                Call("BeginMarqueeSelection", start, false, false);
                Call("UpdateMarqueeSelection", new Point(0, 0));
                Check(paths.SetEquals(new[] { items[^1].Path }), "小于拖动阈值不能提前改变选择");
                Call("EndMarqueeSelection", true);
                Check(paths.SetEquals(new[] { items[^1].Path })
                    && (bool)typeof(MainWindow).GetField("_suppressMarqueeTap", flags)!.GetValue(window)!,
                    "按下后尚未拖动就取消也应阻止随后点击清空原选择");
                Call("BeginMarqueeSelection", start, false, false);
                Call("UpdateMarqueeSelection", end);
                Check(paths.SetEquals(expected), $"{mode} 框选应补齐首尾命中项之间的连续文件");
                Check(overlay.Visibility == Visibility.Visible && !overlay.IsHitTestVisible
                    && Canvas.GetLeft(box) >= 0 && Canvas.GetTop(box) >= 0
                    && box.Width <= scroll.ViewportWidth && box.Height <= scroll.ViewportHeight,
                    "选框必须限制在浏览视口且不挡鼠标输入");
                Call("EndMarqueeSelection", false);
                var targets = ((string Path, bool Folder)[])Call("SelectedFileTargets")!;
                Check(targets.Length == expected.Count && overlay.Visibility == Visibility.Collapsed,
                    "完成框选应更新实际批量操作目标并隐藏选框");
                Call("BeginMarqueeSelection", end, false, false);
                Call("UpdateMarqueeSelection", start);
                Check(paths.SetEquals(expected), "反向拖动必须得到同样的选区");
                Call("EndMarqueeSelection", false);
                Call("SelectItem", items[^1]);
                Call("BeginMarqueeSelection", start, false, true);
                Call("UpdateMarqueeSelection", end);
                Check(paths.SetEquals(expected.Append(items[^1].Path)), "Shift 框选应追加到原选择");
                Call("EndMarqueeSelection", true);
                Check(paths.SetEquals(new[] { items[^1].Path }), "取消框选必须恢复开始前的选择");
                Call("SelectItem", items[0]);
                Call("BeginMarqueeSelection", start, true, false);
                Call("UpdateMarqueeSelection", end);
                Check(paths.SetEquals(expected.Where(path => path != items[0].Path)), "Ctrl 框选应基于开始时选择增减，不反复翻转");
                Call("UpdateMarqueeSelection", end);
                Check(paths.SetEquals(expected.Where(path => path != items[0].Path)), "同一矩形重复更新不能翻转选择");
                Call("EndMarqueeSelection", true);
                int realized = ((Dictionary<UIElement, MediaItem>)typeof(MainWindow).GetField("_realizedItems", flags)!.GetValue(window)!).Count;
                Call("BeginMarqueeSelection", start, false, false);
                Call("UpdateMarqueeSelection", new Point(bounds.Max(b => b.X + b.Width) + 1, bounds.Max(b => b.Bottom) + 1));
                Check(paths.Count == items.Count && ((Dictionary<UIElement, MediaItem>)typeof(MainWindow).GetField("_realizedItems", flags)!.GetValue(window)!).Count == realized,
                    "跨屏框选应包含未实现项且不能创建额外卡片");
                Call("EndMarqueeSelection", true);
                var upperRow = Enumerable.Range(0, bounds.Length).Where(i => Math.Abs(bounds[i].Y - bounds[0].Y) < 0.01).ToArray();
                double lowerY = bounds.First(b => b.Y > bounds[0].Y + 0.01).Y;
                var lowerRow = Enumerable.Range(0, bounds.Length).Where(i => Math.Abs(bounds[i].Y - lowerY) < 0.01).ToArray();
                var upperTail = bounds[upperRow[^1]];
                var crossStart = new Point(upperTail.X + upperTail.Width / 2, upperTail.Y + 1);
                var crossEnd = new Point(bounds.Max(b => b.X + b.Width) + 1, lowerY + 5);
                Call("BeginMarqueeSelection", crossStart, false, false);
                Call("UpdateMarqueeSelection", crossEnd);
                Check(lowerRow.All(i => paths.Contains(items[i].Path)) && paths.Contains(items[upperRow[^1]].Path)
                    && upperRow.Take(upperRow.Length - 1).All(i => !paths.Contains(items[i].Path)),
                    "从上排右侧跨到下排右侧应补齐整条下排，同时不选上排左侧项目");
                Call("EndMarqueeSelection", true);
                Call("BeginMarqueeSelection", new Point(bounds[0].X + 1, bounds[0].Y + 1), false, false);
                Call("UpdateMarqueeSelection", new Point(bounds[0].X + bounds[0].Width - 1, bounds[0].Y + 10));
                Check(paths.SetEquals(new[] { items[0].Path }), "同行内只框中一项时不能扩选整行");
                Call("EndMarqueeSelection", true);
            }
            scroll.ChangeView(null, 0, null, true); await Settle();
            Call("BeginMarqueeSelection", new Point(0, 0), false, false);
            Call("UpdateMarqueeSelection", new Point(100, 100));
            typeof(MainWindow).GetField("_marqueeViewportPoint", flags)!.SetValue(window, new Point(100, scroll.ViewportHeight + 10));
            Call("ScrollMarqueeAtEdge"); await Settle();
            Check(scroll.VerticalOffset > 0, "框选靠近底边应自动滚动");
            Call("ApplyDisplayOptions");
            Check(!(bool)typeof(MainWindow).GetField("_marqueeStarted", flags)!.GetValue(window)!
                && overlay.Visibility == Visibility.Collapsed, "切换布局必须终止框选和自动滚动");
            await (Task)Call("SetDisplayModeAsync", GalleryDisplayMode.Filmstrip)!; await Settle();
            Call("SelectItem", new object[] { null! });
            Call("BeginMarqueeSelection", new Point(0, 0), false, false);
            Call("UpdateMarqueeSelection", new Point(250, 100));
            Check(paths.SetEquals(window.GalleryState.Items.Take(2).Select(item => item.Path))
                && Grid.GetRow(overlay) == Grid.GetRow(scroll), "横排框选应沿水平位置命中并只覆盖缩略图栏");
            Call("EndMarqueeSelection", true);
        }
        finally
        {
            Call("EndMarqueeSelection", true);
            typeof(MainWindow).GetField("_suppressMarqueeTap", flags)!.SetValue(window, false);
            await (Task)Call("SetDisplayModeAsync", originalMode)!;
            scroll.ChangeView(0, 0, null, true); await Settle();
        }
        return checks;
    }
}
