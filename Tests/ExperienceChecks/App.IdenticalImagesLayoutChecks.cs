using System.Reflection;
using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunIdenticalImagesLayoutChecksAsync(MainWindow window)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        var root = (Grid)window.Content;
        var rows = Enumerable.Range(0, 2169).Select(index =>
        {
            string path = $@"C:\layout-fixture\重复图片{index:D4}.jpg";
            return new MainWindow.IdenticalImageRow(new IdenticalImageMatch(path,
                new FileVersion(path, 100, 1), "layout-test", @"C:\layout-fixture\样本.jpg"));
        }).ToArray();
        var results = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple, ItemsSource = rows,
            ItemTemplate = (DataTemplate)root.Resources["IdenticalImageResultTemplate"]
        };
        var header = new Border { Height = 160, Child = new TextBlock { Text = "扫描范围与结果" } };
        var count = new TextBlock { Text = "已选择 2,169 项（包括勾选的样本）" };
        var content = (Grid)typeof(MainWindow).GetMethod("CreateIdenticalImagesContent", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { header, results, count })!;
        // 模拟弹窗扣除标题、按钮、边距之后的真实内容空间，低于内容的期望最大高度。
        var host = new Border { Width = 620, Height = 360, Child = content, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRowSpan(host, 3);
        root.Children.Add(host);
        try
        {
            foreach (double height in new[] { 360d, 280d, 540d })
            {
                host.Height = height;
                root.UpdateLayout();
                await Task.Delay(60);
                root.UpdateLayout();
                Check(content.ActualHeight <= host.ActualHeight + 1, "清理列表内容不能超过弹窗实际分配高度");
                var scroller = Descendants(results).OfType<ScrollViewer>().First();
                results.ScrollIntoView(rows[^1], ScrollIntoViewAlignment.Leading);
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    root.UpdateLayout();
                    scroller.ChangeView(null, scroller.ScrollableHeight, null, disableAnimation: true);
                    await Task.Delay(60);
                }
                root.UpdateLayout();
                var last = results.ContainerFromIndex(rows.Length - 1) as FrameworkElement;
                Check(last is not null, "清理列表滚到底应实现最后一项");
                Check(Descendants(last!).OfType<FileThumbnail>().Single().File?.Path == rows[^1].Path,
                    "清理列表复用到末项时，预览必须绑定该项自己的文件路径");
                var lastBounds = last!.TransformToVisual(results).TransformBounds(new Rect(0, 0, last.ActualWidth, last.ActualHeight));
                var listBounds = results.TransformToVisual(host).TransformBounds(new Rect(0, 0, results.ActualWidth, results.ActualHeight));
                var countBounds = count.TransformToVisual(host).TransformBounds(new Rect(0, 0, count.ActualWidth, count.ActualHeight));
                Check(lastBounds.Top >= -1 && lastBounds.Bottom <= results.ActualHeight + 1,
                    "清理列表滚到底后最后一项必须完整可见");
                Check(listBounds.Bottom <= countBounds.Top && countBounds.Bottom <= host.ActualHeight + 1,
                    "列表和选择计数不能被弹窗底部裁切或重叠");
                Check(Descendants(results).OfType<ListViewItem>().Count() < 100,
                    "上千条清理结果必须保持虚拟化");
            }
            results.ItemsSource = null;
            var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../.."));
            string sample = Path.Combine(workspace, "work", "validation", "media", "image1.jpg");
            var file = new MediaFile(sample, MediaKind.Image) { Version = FileVersion.Read(sample) };
            var thumbnail = new FileThumbnail { File = file, Width = 64, Height = 48 };
            host.Child = thumbnail;
            async Task WaitUntil(Func<bool> predicate)
            {
                for (int attempt = 0; attempt < 100 && !predicate(); attempt++)
                { root.UpdateLayout(); await Task.Delay(30); }
            }
            var image = thumbnail.Children.OfType<Image>().Single();
            var status = thumbnail.Children.OfType<TextBlock>().Single();
            await WaitUntil(() => image.Source is not null);
            Check(image.Source is not null && image.Stretch == Microsoft.UI.Xaml.Media.Stretch.Uniform,
                "待删除条目应加载真实文件的完整缩略图");
            thumbnail.File = rows[0].PreviewFile;
            Check(image.Source is null, "条目复用到另一文件时必须立即清除旧预览");
            await WaitUntil(() => status.Text == "无法预览");
            Check(image.Source is null && status.Text == "无法预览" && status.Visibility == Visibility.Visible,
                "预览失败应显示明确占位，不能显示上一项的图片");
            thumbnail.File = file;
            await WaitUntil(() => image.Source is not null);
            Check(image.Source is not null, "失败条目复用到有效文件应重新加载预览");
            host.Child = null;
            await WaitUntil(() => image.Source is null);
            Check(image.Source is null, "列表预览卸载后必须释放图片引用");
            string longPath = @"C:\layout-fixture\" + string.Concat(Enumerable.Repeat("超长中文目录名称 空格_", 24));
            var scope = (ComboBox)typeof(MainWindow).GetMethod("CreateIdenticalImageScope", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { new[] { longPath, @"C:\layout-fixture", @"C:\" } })!;
            scope.VerticalAlignment = VerticalAlignment.Top;
            host.Child = scope;
            foreach (double width in new[] { 260d, 520d, 340d })
            {
                host.Width = width;
                root.UpdateLayout(); await Task.Delay(60); root.UpdateLayout();
                var popupBorder = (FrameworkElement?)typeof(PathComboBox).GetField("_popupBorder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scope);
                Check(popupBorder is not null, "路径菜单应获取当前 WinUI 模板的实际弹出内容");
                // 独立测量弹出层，不打开用户窗口或发送输入；模拟 Popup 按内容无限宽测量。
                popupBorder!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                popupBorder.Arrange(new Rect(0, 0, popupBorder.DesiredSize.Width, popupBorder.DesiredSize.Height));
                Check(popupBorder.ActualWidth <= scope.ActualWidth + 1 && scope.ActualWidth <= host.ActualWidth + 1,
                    "超长路径菜单不能超出选择框或弹窗分配的宽度");
            }
            Check(Equals(scope.SelectedItem, longPath) && Equals(ToolTipService.GetToolTip(scope), longPath),
                "路径显示省略不能截断实际扫描范围，悬停必须保留完整路径");
            var pathText = (TextBlock)scope.ItemTemplate.LoadContent();
            pathText.DataContext = longPath;
            pathText.Measure(new Size(180, double.PositiveInfinity));
            Check(pathText.Text == longPath && pathText.TextTrimming == TextTrimming.CharacterEllipsis
                && pathText.DesiredSize.Width <= 180 && Equals(ToolTipService.GetToolTip(pathText), longPath),
                "路径菜单行必须单行省略并保留完整路径提示");
            scope.SelectedIndex = 1;
            Check(Equals(scope.SelectedItem, @"C:\layout-fixture") && Equals(ToolTipService.GetToolTip(scope), @"C:\layout-fixture"),
                "切换上级目录应保留完整扫描路径并更新提示");
            // 宿主必须按内容自然测量；之前给宿主固定 Width 会掩盖整张弹窗随路径收缩的问题。
            host.Child = null;
            host.Width = host.Height = double.NaN;
            host.HorizontalAlignment = HorizontalAlignment.Left;
            var scopeRow = new Grid { ColumnSpacing = 8 };
            scopeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scopeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            scopeRow.Children.Add(scope);
            var startButton = new Button { Content = "开始查找", VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetColumn(startButton, 1); scopeRow.Children.Add(startButton);
            var sizingContent = (Grid)typeof(MainWindow).GetMethod("CreateIdenticalImagesContent", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { scopeRow, new ListView(), new TextBlock { Text = "选择扫描范围后开始查找。" } })!;
            var sizeContent = typeof(MainWindow).GetMethod("SizeIdenticalImagesContent", BindingFlags.Static | BindingFlags.NonPublic)!;
            sizeContent.Invoke(null, new object[] { sizingContent, 1100d, 720d });
            host.Child = sizingContent;
            scope.SelectedIndex = 0;
            root.UpdateLayout(); await Task.Delay(60); root.UpdateLayout();
            double stableWidth = host.ActualWidth, stableHeight = host.ActualHeight;
            Check(stableWidth >= 680, "清理弹窗应取得由窗口决定的宽度，不能退回内容最小宽度");
            foreach (int selected in new[] { 1, 2, -1, 0 })
            {
                scope.SelectedIndex = selected;
                root.UpdateLayout(); await Task.Delay(60); root.UpdateLayout();
                Check(Math.Abs(host.ActualWidth - stableWidth) < 1 && Math.Abs(host.ActualHeight - stableHeight) < 1,
                    "长路径、短路径、盘符与暂时空内容之间切换不能改变整个弹窗大小");
            }
            sizeContent.Invoke(null, new object[] { sizingContent, 560d, 720d });
            root.UpdateLayout(); await Task.Delay(60); root.UpdateLayout();
            Check(Math.Abs(host.ActualWidth - 460) < 1, "只有宿主窗口变窄时清理弹窗才随之收缩");
        }
        finally { root.Children.Remove(host); results.ItemsSource = null; }
        return checks;
    }
}
