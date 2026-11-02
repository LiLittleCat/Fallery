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
        string type = details.Format + (audio ? AppText.Get("Audioc69086") : video ? AppText.Get("Video") : AppText.Get("Image"));

        var basic = new List<(string Label, string Value)>
        {
            (AppText.Get("FileName"), Value(AppText.Get("FileName"))),
            (AppText.Get("FileType"), type),
            (AppText.Get("FileSize"), AppText.Format("Bytescf041f", details.Size, details.Length)),
            (AppText.Get("DateCreated"), Value(AppText.Get("DateCreated07aa0e"))),
            (AppText.Get("DateModified"), Value(AppText.Get("DateModified2cbced"))),
            (AppText.Get("FileLocation"), Value(AppText.Get("Folder"))),
        };
        AddDetailsCard(AppText.Get("BasicInformation"), "IconInfo", basic, fileActions: true);

        var media = details.Groups.Where(group => group.Group == (audio ? AppText.Get("Audio") : video ? AppText.Get("Videos") : AppText.Get("Image37e4e4")))
            .SelectMany(group => group.Rows).ToList();
        if (details.Width > 0 && details.Height > 0)
        {
            uint a = details.Width, b = details.Height;
            while (b != 0) (a, b) = (b, a % b);
            media.Add((AppText.Get("AspectRatio"), $"{details.Width / a}:{details.Height / a}"));
        }
        AddDetailsCard(audio ? AppText.Get("AudioInformation") : video ? AppText.Get("VideoInformation") : AppText.Get("ImageInformation"), audio ? "IconAudio" : video ? "IconVideo" : "IconImage", media);
        if (audio)
        {
            LiveText.Set(AudioTitle, "Text", localized => localized.Text = Value(AppText.Get("Title")) is { Length: > 0 } title ? title : ViewerState.CurrentItem!.Name);
            LiveText.Set(AudioSubtitle, "Text", localized => localized.Text = string.Join(" · ", new[] { Value(AppText.Get("Artist")), Value(AppText.Get("Album")) }.Where(value => value.Length > 0)));
        }
        foreach (var (group, rows) in details.Groups.Where(group => group.Group == AppText.Get("Camera")))
            AddDetailsCard(AppText.Get("CameraInformation"), "IconSortTaken", rows);
        foreach (var (group, rows) in details.Groups.Where(group => group.Group == AppText.Get("Location")))
            AddDetailsCard(AppText.Get("Location"), "IconInfo", rows);
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
        LiveText.SetOnce(button, "AutomationProperties.Name", localized => AutomationProperties.SetName(localized, label));
        LiveText.SetOnce(button, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, label));
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
                var name when name == AppText.Get("FileName") => DetailsAction("DetailsRevealButton", "IconOpenFolder", AppText.Get("OpenFileLocation"), DetailsReveal_Click),
                var location when location == AppText.Get("FileLocation") => DetailsAction("DetailsCopyPathButton", "IconCopy", AppText.Get("CopyFullFilePath"), DetailsCopyPath_Click),
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
