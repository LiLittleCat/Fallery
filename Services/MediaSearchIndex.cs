using System.Diagnostics;
using System.IO.Enumeration;
using System.Text;
using Fallery.Models;

namespace Fallery.Services;

public sealed record SearchEntry(string Path, MediaKind Kind, bool Hidden)
{
    public string Name { get; } = System.IO.Path.GetFileName(Path);
    public string Parent => System.IO.Path.GetDirectoryName(Path) ?? Path;
}
public sealed record SearchMatches(IReadOnlyList<SearchEntry> Items, int Total);
public sealed record SearchIndexStatus(int Count, bool Building, bool Limited, int Skipped, string Message);

// 索引只保存路径和类型，不解码媒体。索引与查询均运行于后台，界面只接收有限结果。
public sealed class MediaSearchIndex(string storagePath, int entryLimit = 1_000_000)
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _buildGate = new(1);
    private Dictionary<string, SearchEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private const int Magic = 0x31534946;
    public event Action<SearchIndexStatus>? Progress;
    public SearchIndexStatus Status { get; private set; } = new(0, false, false, 0, "尚未建立索引");

    public static string[] LocalRoots() => DriveInfo.GetDrives()
        .Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable && Ready(d)).Select(d => d.RootDirectory.FullName).ToArray();
    private static bool Ready(DriveInfo drive) { try { return drive.IsReady; } catch (IOException) { return false; } }

    internal static bool TryGetDirectoryVisibility(string path, FileAttributes attributes, bool inheritedHidden, out bool hidden)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        bool volumeRoot = string.Equals(System.IO.Path.TrimEndingDirectorySeparator(fullPath),
            System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetPathRoot(fullPath)!), StringComparison.OrdinalIgnoreCase);
        // Windows 卷根通常带 System/Hidden；这些属性不能阻止遍历或传给所有后代。
        hidden = inheritedHidden || (!volumeRoot && (attributes & FileAttributes.Hidden) != 0);
        return (attributes & FileAttributes.ReparsePoint) == 0
            && (volumeRoot || (attributes & FileAttributes.System) == 0);
    }

    public Task<bool> LoadAsync(CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            if (!File.Exists(storagePath) || new FileInfo(storagePath).Length > 512L * 1024 * 1024) return false;
            using var reader = new BinaryReader(File.OpenRead(storagePath), Encoding.UTF8);
            if (reader.ReadInt32() != Magic) return false;
            bool limited = reader.ReadBoolean();
            int count = reader.ReadInt32();
            if (count < 0 || count > entryLimit) return false;
            var loaded = new Dictionary<string, SearchEntry>(count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                int length = reader.ReadInt32();
                if (length is < 1 or > 32768) return false;
                byte[] bytes = reader.ReadBytes(length);
                if (bytes.Length != length) return false;
                string path = Encoding.UTF8.GetString(bytes);
                var kind = (MediaKind)reader.ReadByte();
                bool hidden = reader.ReadBoolean();
                if (!System.IO.Path.IsPathFullyQualified(path) || !Enum.IsDefined(kind)) return false;
                loaded[path] = new(path, kind, hidden);
            }
            lock (_gate) _entries = loaded;
            Report(new(loaded.Count, false, limited, 0, "已载入上次索引，正在检查更新"));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }, ct);

    public async Task BuildAsync(IReadOnlyList<string> roots, CancellationToken ct)
    {
        await _buildGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                var building = new Dictionary<string, SearchEntry>(StringComparer.OrdinalIgnoreCase);
                // 首次索引逐批可查；有旧索引时继续提供旧结果，完成后原子替换。
                lock (_gate) if (_entries.Count == 0) _entries = building;
                bool limited = false;
                int skipped = 0;
                int traversed = 0;
                var clock = Stopwatch.StartNew();
                // 逐层遍历各磁盘，避免某个盘的深层目录耗尽预算后才开始其他盘。
                var pending = new Queue<(string Path, bool Hidden)>(roots.Distinct(StringComparer.OrdinalIgnoreCase).Select(p => (System.IO.Path.GetFullPath(p), false)));
                Report(new(Status.Count, true, false, 0, "正在建立全电脑索引…"));
                while (pending.TryDequeue(out var directory))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(directory.Path);
                        if (!TryGetDirectoryVisibility(directory.Path, attributes, directory.Hidden, out bool parentHidden))
                        { skipped++; continue; }
                        foreach (var file in new DirectoryInfo(directory.Path).EnumerateFileSystemInfos("*", new EnumerationOptions
                        { IgnoreInaccessible = false, AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint }))
                        {
                            ct.ThrowIfCancellationRequested();
                            bool folder = (file.Attributes & FileAttributes.Directory) != 0;
                            bool hidden = parentHidden || (file.Attributes & FileAttributes.Hidden) != 0;
                            if (folder && FolderScanner.IsExcludedDirectory(file.FullName)) continue;
                            MediaKind? kind = folder ? MediaKind.Folder : FolderScanner.GetMediaKind(file.FullName);
                            if (kind is { } actual)
                            {
                                lock (_gate) building[file.FullName] = new(file.FullName, actual, hidden);
                                if (building.Count >= entryLimit) { limited = true; break; }
                            }
                            if (folder) pending.Enqueue((file.FullName, hidden));
                        }
                        traversed++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                    if (limited) break;
                    if (clock.ElapsedMilliseconds >= 500)
                    {
                        Report(new(building.Count, true, false, skipped, $"正在建立索引 · {building.Count:N0} 项"));
                        clock.Restart();
                    }
                }
                ct.ThrowIfCancellationRequested();
                if (traversed == 0)
                {
                    int count;
                    lock (_gate) count = _entries.Count;
                    Report(new(count, false, true, skipped, "未能读取任何索引目录，请检查磁盘是否可访问后刷新"));
                    return;
                }
                lock (_gate) _entries = building;
                string message = limited ? $"已到 {entryLimit:N0} 项上限，结果不完整" : $"索引就绪 · {building.Count:N0} 项 · 刷新可同步文件变动";
                if (skipped > 0) message += $" · {skipped} 个目录不可访问";
                try { Save(building.Values, ct, limited); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { message += " · 索引未能保存"; }
                Report(new(building.Count, false, limited, skipped, message));
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            int count;
            lock (_gate) count = _entries.Count;
            Report(new(count, false, true, Status.Skipped, "索引更新已停止，已有结果仍可搜索"));
            throw;
        }
        finally { _buildGate.Release(); }
    }

    private void Save(ICollection<SearchEntry> entries, CancellationToken ct, bool limited)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(storagePath)!);
        string temporary = storagePath + ".tmp";
        try
        {
            using (var writer = new BinaryWriter(File.Create(temporary), Encoding.UTF8))
            {
                writer.Write(Magic); writer.Write(limited); writer.Write(entries.Count);
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    byte[] path = Encoding.UTF8.GetBytes(entry.Path);
                    writer.Write(path.Length); writer.Write(path); writer.Write((byte)entry.Kind); writer.Write(entry.Hidden);
                }
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, storagePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task RemovePathsAsync(IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        var removed = paths.Select(path => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        await _buildGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                SearchEntry[] snapshot;
                lock (_gate)
                {
                    foreach (string key in _entries.Keys.ToArray())
                    {
                        for (string? path = key; path is not null; path = System.IO.Path.GetDirectoryName(path))
                            if (removed.Contains(path)) { _entries.Remove(key); break; }
                    }
                    snapshot = _entries.Values.ToArray();
                }
                string message = $"索引已同步删除 · {snapshot.Length:N0} 项";
                try { Save(snapshot, ct, Status.Limited); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { message += " · 索引未能保存"; }
                Report(new(snapshot.Length, false, Status.Limited, Status.Skipped, message));
            }, ct).ConfigureAwait(false);
        }
        finally { _buildGate.Release(); }
    }

    private void Report(SearchIndexStatus status) { Status = status; Progress?.Invoke(status); }

    public Task<SearchMatches> SearchAsync(string query, bool showHidden, CancellationToken ct, int limit = 300)
        => Task.Run(() => Search(query, showHidden, ct, limit), ct);

    private SearchMatches Search(string query, bool showHidden, CancellationToken ct, int limit)
    {
        QueryTerm[] terms = Tokens(query.Trim()).Select(ParseTerm).ToArray();
        if (terms.Length == 0 || limit < 1) return new([], 0);
        var comparer = Comparer<(int Score, string Name, string Path)>.Create((a, b) =>
        {
            int compare = b.Score.CompareTo(a.Score);
            if (compare == 0) compare = StringComparer.OrdinalIgnoreCase.Compare(b.Name, a.Name);
            return compare != 0 ? compare : StringComparer.OrdinalIgnoreCase.Compare(b.Path, a.Path);
        });
        var best = new PriorityQueue<SearchEntry, (int, string, string)>(comparer);
        int total = 0;
        lock (_gate)
        {
            foreach (var entry in _entries.Values)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Hidden && !showHidden) continue;
                int score = Match(entry, terms);
                if (score < 0) continue;
                total++;
                best.Enqueue(entry, (score, entry.Name, entry.Path));
                if (best.Count > limit) best.Dequeue();
            }
        }
        var ordered = new List<SearchEntry>(best.Count);
        while (best.TryDequeue(out var entry, out _)) ordered.Add(entry);
        ordered.Reverse();
        return new(ordered, total);
    }

    private static IEnumerable<string> Tokens(string query)
    {
        var token = new StringBuilder();
        bool quote = false;
        foreach (char c in query.Take(512))
        {
            if (c == '"') { quote = !quote; continue; }
            if (char.IsWhiteSpace(c) && !quote)
            { if (token.Length > 0) { yield return token.ToString(); token.Clear(); } }
            else token.Append(c);
        }
        if (token.Length > 0) yield return token.ToString();
    }

    private sealed record QueryTerm(string Pattern, bool Path, bool Wildcard, HashSet<string>? Extensions, bool IsType, MediaKind? Kind);

    private static QueryTerm ParseTerm(string term)
    {
        if (term.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            return new("", false, false, term[4..].Split(';').Select(ext => ext.TrimStart('.')).ToHashSet(StringComparer.OrdinalIgnoreCase), false, null);
        if (term.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            return new("", false, false, null, true, term[5..].ToLowerInvariant() switch
            { "photo" or "image" or "图片" => MediaKind.Image, "video" or "视频" => MediaKind.Video, "folder" or "文件夹" => MediaKind.Folder, "audio" or "music" or "音频" => MediaKind.Audio, _ => null });
        bool path = term.StartsWith("path:", StringComparison.OrdinalIgnoreCase) || term.Contains('\\') || term.Contains('/');
        string pattern = (term.StartsWith("path:", StringComparison.OrdinalIgnoreCase) ? term[5..] : term).Replace('/', '\\');
        return new(pattern, path, pattern.Contains('*') || pattern.Contains('?'), null, false, null);
    }

    private static int Match(SearchEntry entry, QueryTerm[] terms)
    {
        int score = 0;
        string name = entry.Name;
        foreach (var term in terms)
        {
            if (term.Extensions is { } extensions)
            {
                if (entry.Kind == MediaKind.Folder || !extensions.Contains(System.IO.Path.GetExtension(name).TrimStart('.'))) return -1;
                continue;
            }
            if (term.IsType)
            {
                if (term.Kind != entry.Kind) return -1;
                continue;
            }
            string pattern = term.Pattern;
            string target = term.Path ? entry.Path : name;
            if (term.Wildcard ? !FileSystemName.MatchesSimpleExpression(pattern, target, ignoreCase: true)
                : !target.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return -1;
            score += target.Equals(pattern, StringComparison.OrdinalIgnoreCase) ? 0 : target.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        }
        return score;
    }
}
