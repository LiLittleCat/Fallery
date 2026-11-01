using System.Text.Json;
using Fallery.Models;

namespace Fallery.Services.Playback;

/// <summary>
/// 每个视频的播放进度，按完整路径、大小和修改时间校验，文件被替换后不沿用旧进度。
/// 最多保留 500 条，按最近写入淘汰；写入在后台串行完成。
/// </summary>
public sealed class ResumeStore(string path)
{
    public const int Limit = 500;
    private static readonly TimeSpan Edge = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _gate = new(1);
    private List<Entry>? _entries;

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fallery", "resume.json");
    public static ResumeStore Shared { get; } = new(DefaultPath);

    public sealed record Entry(string Path, long Length, long ModifiedTicks, double Seconds, long SavedTicks);

    public async Task<TimeSpan?> GetAsync(FileVersion version)
    {
        var entries = await LoadAsync();
        lock (entries)
        {
            var hit = entries.FirstOrDefault(e => Matches(e, version));
            return hit is null ? null : TimeSpan.FromSeconds(hit.Seconds);
        }
    }

    /// <summary>开头和结尾附近不记录，看完的视频下次从头播放。</summary>
    public async Task SaveAsync(FileVersion version, TimeSpan position, TimeSpan duration)
    {
        var entries = await LoadAsync();
        List<Entry> snapshot;
        lock (entries)
        {
            entries.RemoveAll(e => string.Equals(e.Path, version.Path, StringComparison.Ordinal));
            if (duration > TimeSpan.Zero && position > Edge && position < duration - Edge)
                entries.Insert(0, new(version.Path, version.Length, version.ModifiedTicks, position.TotalSeconds, DateTime.UtcNow.Ticks));
            if (entries.Count > Limit) entries.RemoveRange(Limit, entries.Count - Limit);
            snapshot = [.. entries];
        }
        await _gate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
                    var temporary = path + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
                    File.Move(temporary, path, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            });
        }
        finally { _gate.Release(); }
    }

    private static bool Matches(Entry entry, FileVersion version) =>
        string.Equals(entry.Path, version.Path, StringComparison.Ordinal) && entry.Length == version.Length && entry.ModifiedTicks == version.ModifiedTicks;

    private async Task<List<Entry>> LoadAsync()
    {
        if (_entries is not null) return _entries;
        var loaded = await Task.Run(() =>
        {
            try { return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? []; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
        });
        return _entries ??= loaded;
    }
}
