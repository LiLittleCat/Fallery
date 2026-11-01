using Fallery.Controls;
using Fallery.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private void RenderViewerDetails(MediaDetails details)
    {
        DetailsContent.Children.Clear();
        var allRows = details.Groups.SelectMany(group => group.Rows).ToArray();
        string Value(string label) => allRows.FirstOrDefault(row => row.Label == label).Value ?? "";
        bool video = ViewerState.CurrentItem?.IsVideo == true;
        bool audio = ViewerState.CurrentItem?.IsAudio == true;
        string type = details.Format + (audio ? " 音频" : video ? " 视频" : " 图片");

        var basic = new List<(string Label, string Value)>
        {
            ("文件名", Value("文件名")),
            ("文件类型", type),
            ("文件大小", $"{details.Size}（{details.Length:N0} 字节）"),
            ("创建时间", Value("创建日期")),
            ("修改时间", Value("修改日期")),
            ("文件位置", Value("文件夹")),
        };
        AddDetailsCard("基本信息", "IconInfo", basic, fileActions: true);

        var media = details.Groups.Where(group => group.Group == (audio ? "音频" : video ? "视频" : "图像"))
            .SelectMany(group => group.Rows).ToList();
        if (details.Width > 0 && details.Height > 0)
        {
            uint a = details.Width, b = details.Height;
            while (b != 0) (a, b) = (b, a % b);
            media.Add(("宽高比", $"{details.Width / a}:{details.Height / a}"));
        }
        AddDetailsCard(audio ? "音频信息" : video ? "视频信息" : "图像信息", audio ? "IconAudio" : video ? "IconVideo" : "IconImage", media);
        if (audio)
        {
            AudioTitle.Text = Value("标题") is { Length: > 0 } title ? title : ViewerState.CurrentItem!.Name;
            AudioSubtitle.Text = string.Join(" · ", new[] { Value("艺术家"), Value("专辑") }.Where(value => value.Length > 0));
        }
        foreach (var (group, rows) in details.Groups.Where(group => group.Group == "拍摄"))
            AddDetailsCard("拍摄信息", "IconSortTaken", rows);
        foreach (var (group, rows) in details.Groups.Where(group => group.Group == "地理位置"))
            AddDetailsCard("地理位置", "IconInfo", rows);
    }

    private TextBlock DetailsValue(string text) => new()
    {
        Text = text,
        Style = (Style)Root.Resources["DetailsValueStyle"],
    };

    private Button DetailsAction(string name, string icon, string label, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Name = name,
            Style = (Style)Root.Resources["IconButtonStyle"],
            Content = FluentIcons.Create(icon, 15),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -4, -4, -4),
        };
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Click += handler;
        return button;
    }

    private void AddDetailsCard(string title, string glyph, IEnumerable<(string Label, string Value)> rows, bool fileActions = false)
    {
        var values = rows.Where(row => row.Value.Length > 0).ToArray();
        if (values.Length == 0) return;
        var section = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 20) };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        heading.Children.Add(FluentIcons.Create(glyph, 16));
        heading.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        section.Children.Add(heading);

        var content = new StackPanel { Spacing = 10 };
        foreach (var (label, value) in values)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = label, Style = (Style)Root.Resources["DetailsLabelStyle"],
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0),
            });
            var text = DetailsValue(value);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Button? action = fileActions ? label switch
            {
                "文件名" => DetailsAction("DetailsRevealButton", "IconOpenFolder", "打开所在位置", DetailsReveal_Click),
                "文件位置" => DetailsAction("DetailsCopyPathButton", "IconCopy", "复制完整文件路径", DetailsCopyPath_Click),
                _ => null,
            } : null;
            if (action is not null)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(action, 2);
                row.Children.Add(action);
            }
            content.Children.Add(row);
        }
        section.Children.Add(new Border { Style = (Style)Root.Resources["DetailsCardStyle"], Child = content });
        DetailsContent.Children.Add(section);
    }
}
