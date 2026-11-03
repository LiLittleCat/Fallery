using System.Text.RegularExpressions;

namespace Fallery.Services;

public enum ReleaseNotesBlockKind { Paragraph, Heading, ListItem, Code }
[Flags]
public enum ReleaseNotesStyle { Normal = 0, Bold = 1, Italic = 2, Code = 4 }
public sealed record ReleaseNotesSpan(string Text, ReleaseNotesStyle Style = ReleaseNotesStyle.Normal, Uri? Link = null);
public sealed record ReleaseNotesBlock(ReleaseNotesBlockKind Kind, IReadOnlyList<ReleaseNotesSpan> Spans, int Level = 0, string Prefix = "");
public sealed record ReleaseNotesSelection(string Markdown, bool IsFallback);

/// <summary>仅解释发行说明使用的 Markdown 子集，不执行 HTML，也不加载远程图片。</summary>
public static class ReleaseNotesDocument
{
    private const int MaximumLength = 64_000;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Lazy<string> ChineseHistory = new(() =>
    {
        using var stream = typeof(ReleaseNotesDocument).Assembly.GetManifestResourceStream("Fallery.ReleaseNotes.zh-CN.md");
        using var reader = stream is null ? null : new StreamReader(stream);
        return reader?.ReadToEnd() ?? "";
    });

    private static Match Match(string text, string pattern) => Regex.Match(text, pattern, RegexOptions.CultureInvariant, MatchTimeout);

    public static ReleaseNotesSelection Select(string? markdown, string version, string language)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return new("", false);
        string bounded = markdown[..Math.Min(markdown.Length, MaximumLength)];
        string locale = AppText.NormalizeLanguage(language);
        string Section(string code)
        {
            string marker = "<!-- fallery-notes:" + code + " -->";
            int start = bounded.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return "";
            start += marker.Length;
            int end = bounded.IndexOf("<!-- /fallery-notes -->", start, StringComparison.Ordinal);
            return end < 0 ? "" : bounded[start..end].Trim();
        }
        string selected = Section(locale);
        if (selected.Length > 0) return new(selected, false);
        // 已发布包内只有英文；随程序附带的历史翻译按版本匹配，不改旧发行资产。
        if (locale == "zh-CN")
        {
            var historical = Match(ChineseHistory.Value, @"(?ms)^## \[" + Regex.Escape(version) + @"\] - [^\r\n]+\r?\n(.*?)(?=^## |^\[[^\r\n]+\]:|\z)");
            if (historical.Success && !string.IsNullOrWhiteSpace(historical.Groups[1].Value))
                return new(historical.Groups[1].Value.Trim(), false);
        }
        string alternative = Section(locale == "en-US" ? "zh-CN" : "en-US");
        if (alternative.Length > 0) return new(alternative, true);
        return new(bounded, locale != "en-US");
    }

    public static IReadOnlyList<ReleaseNotesBlock> Parse(string markdown)
    {
        try { return ParseCore(markdown); }
        catch (RegexMatchTimeoutException)
        {
            // 远端格式异常不能使设置页崩溃；退回有界纯文本。
            return [new(ReleaseNotesBlockKind.Paragraph, [new(markdown[..Math.Min(markdown.Length, MaximumLength)])])];
        }
    }

    private static IReadOnlyList<ReleaseNotesBlock> ParseCore(string markdown)
    {
        string text = markdown[..Math.Min(markdown.Length, MaximumLength)].Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"<!--.*?(?:-->|\z)", "", RegexOptions.Singleline, MatchTimeout);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split('\n');
        foreach (string line in lines)
        {
            var reference = Match(line, @"^\s*\[([^\]]+)\]:\s*(\S+)");
            if (reference.Success) references[reference.Groups[1].Value] = reference.Groups[2].Value.Trim('<', '>');
        }
        var blocks = new List<ReleaseNotesBlock>();
        var paragraph = new List<string>();
        bool inCode = false;
        string fence = "";
        void Flush()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new(inCode ? ReleaseNotesBlockKind.Code : ReleaseNotesBlockKind.Paragraph,
                inCode ? [new(string.Join('\n', paragraph), ReleaseNotesStyle.Code)] : ParseInline(string.Join(' ', paragraph), references)));
            paragraph.Clear();
        }
        foreach (string raw in lines)
        {
            if (blocks.Count >= 128) break;
            string line = raw.Trim();
            if (line.StartsWith("```") || line.StartsWith("~~~"))
            {
                if (!inCode) { Flush(); fence = line[..3]; inCode = true; }
                else if (line.StartsWith(fence)) { Flush(); inCode = false; }
                else paragraph.Add(raw);
                continue;
            }
            if (inCode) { paragraph.Add(raw); continue; }
            if (line.Length == 0) { Flush(); continue; }
            if (Match(line, @"^\[[^\]]+\]:").Success) continue;
            var heading = Match(line, @"^(#{1,6})\s+(.+?)(?:\s+#+)?$");
            var item = Match(line, @"^(?:([-+*])|([0-9]{1,9})[.)])\s+(.+)$");
            if (heading.Success)
            {
                Flush();
                string title = heading.Groups[2].Value switch
                {
                    "Added" => AppText.Get("ReleaseNotesAdded"),
                    "Changed" => AppText.Get("ReleaseNotesChanged"),
                    "Deprecated" => AppText.Get("ReleaseNotesDeprecated"),
                    "Removed" => AppText.Get("ReleaseNotesRemoved"),
                    "Fixed" => AppText.Get("ReleaseNotesFixed"),
                    "Security" => AppText.Get("ReleaseNotesSecurity"),
                    var original => original
                };
                blocks.Add(new(ReleaseNotesBlockKind.Heading, ParseInline(title, references), heading.Groups[1].Length));
            }
            else if (item.Success)
            {
                Flush();
                blocks.Add(new(ReleaseNotesBlockKind.ListItem, ParseInline(item.Groups[3].Value, references),
                    Prefix: item.Groups[2].Success ? item.Groups[2].Value + "." : "•"));
            }
            else if (raw.Length > 0 && char.IsWhiteSpace(raw[0]) && paragraph.Count == 0 && blocks.LastOrDefault()?.Kind == ReleaseNotesBlockKind.ListItem)
            {
                var previous = blocks[^1];
                blocks[^1] = previous with { Spans = [.. previous.Spans, new(" "), .. ParseInline(line, references)] };
            }
            else paragraph.Add(line);
        }
        if (blocks.Count < 128) Flush();
        return blocks;
    }

    private static IReadOnlyList<ReleaseNotesSpan> ParseInline(string text, Dictionary<string, string> references, ReleaseNotesStyle style = ReleaseNotesStyle.Normal, int depth = 0)
    {
        if (depth > 4) return [new(text, style)];
        const string pattern = @"(?<link>\[(?<label>[^\]\n]+)\](?:\((?<url>[^)\s]+)\)|\[(?<ref>[^\]\n]+)\]))|(?<bold>\*\*(?<boldText>.+?)\*\*|__(?<boldText>.+?)__)|(?<code>`(?<codeText>[^`]+)`)|(?<italic>\*(?<italicText>[^*]+)\*|(?<!\w)_(?<italicText>[^_]+)_(?!\w))";
        var spans = new List<ReleaseNotesSpan>();
        int position = 0;
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.CultureInvariant, MatchTimeout))
        {
            if (match.Index > position) spans.Add(new(text[position..match.Index], style));
            if (match.Groups["link"].Success)
            {
                string destination = match.Groups["url"].Value;
                if (match.Groups["ref"].Success) destination = references.GetValueOrDefault(match.Groups["ref"].Value, "");
                Uri? link = Uri.TryCreate(destination, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;
                spans.AddRange(ParseInline(match.Groups["label"].Value, references, style, depth + 1).Select(span => span with { Link = link }));
            }
            else if (match.Groups["code"].Success) spans.Add(new(match.Groups["codeText"].Value, style | ReleaseNotesStyle.Code));
            else
            {
                bool bold = match.Groups["bold"].Success;
                spans.AddRange(ParseInline(match.Groups[bold ? "boldText" : "italicText"].Value, references,
                    style | (bold ? ReleaseNotesStyle.Bold : ReleaseNotesStyle.Italic), depth + 1));
            }
            position = match.Index + match.Length;
        }
        if (position < text.Length) spans.Add(new(text[position..], style));
        return spans;
    }
}
