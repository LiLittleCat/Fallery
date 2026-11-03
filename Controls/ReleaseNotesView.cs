using Fallery.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Fallery.Controls;

/// <summary>原生文本布局跟随主题，语言变化只重建说明，不影响更新任务。</summary>
public sealed class ReleaseNotesView : StackPanel
{
    private string _markdown = "";
    private string _version = "";
    private string? _renderedLanguage;

    public ReleaseNotesView()
    {
        Spacing = 8;
        MaxWidth = 400;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        LiveText.Set(this, "ReleaseNotes", view => view.Render());
    }

    public void SetNotes(AppUpdateRelease? release)
    {
        string markdown = release?.Notes ?? "";
        string version = release?.Version ?? "";
        if (_markdown == markdown && _version == version && _renderedLanguage == AppText.Language) return;
        _markdown = markdown;
        _version = version;
        Render();
    }

    private void Render()
    {
        _renderedLanguage = AppText.Language;
        Children.Clear();
        var selected = ReleaseNotesDocument.Select(_markdown, _version, AppText.Language);
        Visibility = string.IsNullOrWhiteSpace(selected.Markdown) ? Visibility.Collapsed : Visibility.Visible;
        if (Visibility == Visibility.Collapsed) return;
        if (selected.IsFallback)
            Children.Add(new TextBlock { Text = AppText.Get("ReleaseNotesTranslationUnavailable"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75 });
        foreach (var block in ReleaseNotesDocument.Parse(selected.Markdown))
        {
            var text = new RichTextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
            if (block.Kind == ReleaseNotesBlockKind.Heading)
            {
                text.FontSize = block.Level == 1 ? 18 : 15;
                text.FontWeight = FontWeights.SemiBold;
                text.Margin = new Thickness(0, Children.Count == 0 ? 0 : 4, 0, 0);
            }
            var paragraph = new Paragraph();
            foreach (var span in block.Spans)
            {
                var run = new Run { Text = span.Text };
                if (span.Style.HasFlag(ReleaseNotesStyle.Bold)) run.FontWeight = FontWeights.SemiBold;
                if (span.Style.HasFlag(ReleaseNotesStyle.Italic)) run.FontStyle = FontStyle.Italic;
                if (span.Style.HasFlag(ReleaseNotesStyle.Code)) run.FontFamily = new FontFamily("Consolas");
                if (span.Link is { } uri)
                {
                    var link = new Hyperlink { NavigateUri = uri };
                    link.Inlines.Add(run);
                    paragraph.Inlines.Add(link);
                }
                else paragraph.Inlines.Add(run);
            }
            text.Blocks.Add(paragraph);
            if (block.Kind != ReleaseNotesBlockKind.ListItem) { Children.Add(text); continue; }
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = block.Prefix, FontSize = 14, MinWidth = 12 });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Children.Add(row);
        }
    }
}
