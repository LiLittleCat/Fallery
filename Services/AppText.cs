using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fallery.Services;

/// <summary>共享界面语言；媒体名称、路径和搜索语法不参与翻译。</summary>
public static class AppText
{
    private static readonly IReadOnlyDictionary<string, string> Chinese = Load("zh-CN");
    private static readonly IReadOnlyDictionary<string, string> English = Load("en-US");
    private static string _language = "zh-CN";
    public static string Language => _language;
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(_language);
    public static bool IsChanging { get; private set; }
    public static long Revision { get; private set; }
    public static event Action<string>? Changed;
    public static string NormalizeLanguage(string? value) => value == "en-US" ? "en-US" : "zh-CN";

    public static void SetLanguage(string? language)
    {
        string normalized = NormalizeLanguage(language);
        if (_language == normalized) return;
        string previous = _language;
        _language = normalized;
        Revision++;
        IsChanging = true;
        try { Changed?.Invoke(previous); }
        finally { IsChanging = false; }
    }

    public static string Get(string key) => (_language == "en-US" ? English : Chinese).TryGetValue(key, out var value)
        ? value : Chinese.TryGetValue(key, out var fallback) ? fallback : key;
    public static string Format(string key, params object?[] arguments) => string.Format(Culture, Get(key), arguments);

    // 只用于已有的应用状态消息；匹配整条模板，参数（包括用户文件名和路径）原样保留。
    public static string TranslateStatus(string text, string previousLanguage)
    {
        if (previousLanguage == Language || string.IsNullOrEmpty(text)) return text;
        var source = previousLanguage == "en-US" ? English : Chinese;
        foreach (var (key, value) in source)
            if (value == text) return Get(key);
        foreach (var (key, value) in source.OrderByDescending(pair => pair.Value.Length))
        {
            var placeholders = Regex.Matches(value, @"\{(\d+)(?:[^{}]*)\}");
            if (placeholders.Count == 0 || value.Length < 5) continue;
            string pattern = "\\A";
            int start = 0;
            foreach (Match placeholder in placeholders)
            {
                pattern += Regex.Escape(value[start..placeholder.Index]) + "([\\s\\S]*?)";
                start = placeholder.Index + placeholder.Length;
            }
            pattern += Regex.Escape(value[start..]) + "\\z";
            var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            if (!match.Success) continue;
            var arguments = new object?[placeholders.Max(p => int.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture)) + 1];
            for (int i = 0; i < placeholders.Count; i++)
                arguments[int.Parse(placeholders[i].Groups[1].Value, CultureInfo.InvariantCulture)] = match.Groups[i + 1].Value;
            return Format(key, arguments);
        }
        return text;
    }

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using var stream = typeof(AppText).Assembly.GetManifestResourceStream($"Fallery.Strings.{language}.json")
            ?? throw new InvalidOperationException($"Missing language resource: {language}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
