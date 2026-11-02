namespace Fallery.Services;

/// <summary>按目录名跳过扫描；发布后只读的快照可由后台扫描并发访问。</summary>
public static class FolderExclusionRules
{
    public static IReadOnlyList<string> Defaults { get; } = Array.AsReadOnly(new[]
    {
        ".git", ".svn", ".hg", ".jj",
        "node_modules", "bower_components", ".npm", ".pnpm-store", ".yarn", ".bun", ".nvm", ".deno",
        ".next", ".nuxt", ".turbo", ".parcel-cache", ".svelte-kit", ".angular", ".expo",
        ".cargo", ".rustup", ".gradle", ".m2", ".ivy2", ".sbt", ".sdkman", ".konan",
        "__pycache__", ".venv", "venv", "site-packages", ".tox", ".nox", ".mypy_cache", ".pytest_cache", ".ruff_cache",
        ".conda", "miniconda3", "anaconda3", ".pyenv", ".rbenv", ".gem", ".pub-cache", ".dart_tool",
        ".nuget", ".dotnet", ".vs", ".idea", ".vscode", ".vscode-server", ".fleet",
        "Pods", "DerivedData", ".swiftpm", "CMakeFiles", ".terraform", ".docker", ".android", ".cache",
    });

    private static readonly object Gate = new();
    private static HashSet<string> _names = new(Defaults, StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;
    public static string[] Names => Volatile.Read(ref _names).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name)
        && name == name.Trim() && name.Length <= 255 && name is not ("." or "..")
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains('/') && !name.Contains('\\');

    // null 表示旧设置没有此项；显式空数组表示用户关闭了全部默认规则。
    public static string[] Normalize(IEnumerable<string>? names) => (names ?? Defaults)
        .Where(IsValidName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public static void Initialize(IEnumerable<string>? names)
    {
        lock (Gate)
        {
            if (_initialized) return;
            _names = new(Normalize(names), StringComparer.OrdinalIgnoreCase);
            _initialized = true;
        }
    }

    public static bool SetNames(IEnumerable<string> names)
    {
        var next = new HashSet<string>(Normalize(names), StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            if (_names.SetEquals(next)) return false;
            Volatile.Write(ref _names, next);
            _initialized = true;
            return true;
        }
    }

    public static bool Contains(string path) => Volatile.Read(ref _names)
        .Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
}
