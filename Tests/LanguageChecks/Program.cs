using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Fallery.Services;

int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    checks++;
}
Dictionary<string, string> Read(string language)
{
    using var stream = typeof(AppText).Assembly.GetManifestResourceStream($"Fallery.Strings.{language}.json")!;
    return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
}
var zh = Read("zh-CN");
var en = Read("en-US");
Check(zh.Keys.Order().SequenceEqual(en.Keys.Order()), "Language resource keys differ");
string workspace = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var sources = Directory.GetFiles(workspace, "*.cs").Concat(new[] { "Controls", "Models", "Services", "ViewModels", "Views" }
    .SelectMany(folder => Directory.GetFiles(Path.Combine(workspace, folder), "*.cs", SearchOption.AllDirectories)));
foreach (string file in sources)
    foreach (Match reference in Regex.Matches(File.ReadAllText(file), "AppText\\.(?:Get|Format)\\(\"([^\"]+)\""))
        Check(zh.ContainsKey(reference.Groups[1].Value), $"Unknown resource in {file}: {reference.Groups[1].Value}");
foreach (string file in Directory.GetFiles(workspace, "*.xaml").Concat(new[] { "Controls", "Views" }
    .SelectMany(folder => Directory.GetFiles(Path.Combine(workspace, folder), "*.xaml", SearchOption.AllDirectories))))
    foreach (var property in XDocument.Load(file).Descendants().Attributes().Where(attribute => attribute.Name.LocalName == "Localization.Keys"))
        foreach (string binding in property.Value.Split(';'))
            Check(zh.ContainsKey(binding.Split('=')[1]), $"Unknown XAML resource in {file}: {binding}");
foreach (string key in zh.Keys)
{
    Check(!string.IsNullOrWhiteSpace(en[key]), $"Empty English translation: {key}");
    Check(!Regex.IsMatch(en[key], "[\u4e00-\u9fff]"), $"Untranslated English resource: {key}");
    var source = Regex.Matches(zh[key], @"\{(\d+)[^{}]*\}").Select(m => m.Groups[1].Value).Order();
    var translated = Regex.Matches(en[key], @"\{(\d+)[^{}]*\}").Select(m => m.Groups[1].Value).Order();
    Check(source.SequenceEqual(translated), $"Format placeholders differ: {key}");
    if (source.Any())
    {
        int count = source.Select(int.Parse).Max() + 1;
        AppText.SetLanguage("en-US");
        Check(!string.IsNullOrEmpty(AppText.Format(key, Enumerable.Repeat<object?>(12, count).ToArray())), $"Bad format: {key}");
    }
}
AppText.SetLanguage("zh-CN");
Check(AppText.NormalizeLanguage(null) == "zh-CN" && AppText.NormalizeLanguage("fr-FR") == "zh-CN", "Invalid languages must fall back to Chinese");
Check(AppText.Get("Settings") == "设置", "Chinese lookup");
var canceled = new LanguagePreview();
canceled.Select("en-US");
Check(AppText.Get("Settings") == "Settings", "Immediate switch");
canceled.Finish(false);
Check(AppText.Language == "zh-CN", "Cancel restores language");
var kept = new LanguagePreview();
kept.Select("en-US");
kept.Finish(true);
Check(AppText.Language == "en-US", "Done retains language");
var older = new LanguagePreview();
older.Select("zh-CN");
var newer = new LanguagePreview();
newer.Select("en-US");
older.Finish(false);
Check(AppText.Language == "en-US", "Older dialog must not undo another window's choice");
string path = @"E:\设置\文件夹\English 图片.jpg";
AppText.SetLanguage("zh-CN");
string message = AppText.Format("ScreenshotSaved", path);
AppText.SetLanguage("en-US");
Check(AppText.TranslateStatus(message, "zh-CN") == "Screenshot saved: " + path, "File paths must survive status translation unchanged");
Check(AppText.Format("ItemsSelected", 1234).Contains("1,234"), "English numeric formatting");
AppText.SetLanguage("zh-CN");
Console.WriteLine($"PASS: {checks} language checks (resource parity, formats, live selection and cancellation)");
