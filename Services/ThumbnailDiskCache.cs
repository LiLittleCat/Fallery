using System.Security.Cryptography;
using System.Text;
using Fallery.Models;

namespace Fallery.Services;

// 只保存系统生成的压缩预览流，不复制原文件；所有磁盘操作都在后台串行执行。
public sealed class ThumbnailDiskCache(string directory, long byteLimit = 512L * 1024 * 1024, int countLimit = 10_000)
{
    public const int MaxEntryBytes = 4 * 1024 * 1024;
    private const uint Magic = 0x31544846; // FHT1
    private readonly object _gate = new();
    private readonly Dictionary<string, (long Bytes, DateTime Used, DateTime Persisted)> _entries = new();
    private bool _initialized;
    private long _bytes;
    private int _generation;
    public int Generation => Volatile.Read(ref _generation);

    private static string Key(FileVersion version) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{version.Path}\n{version.Length}\n{version.ModifiedTicks}"))) + ".thumb";

    public Task<byte[]?> ReadAsync(FileVersion version, uint size, CancellationToken ct) => Task.Run(() =>
    {
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                Initialize();
                string name = Key(version), path = Path.Combine(directory, name);
                if (!_entries.ContainsKey(name)) return null;
                using var input = new BinaryReader(File.OpenRead(path));
                if (input.BaseStream.Length is <= 8 or > MaxEntryBytes + 8 || input.ReadUInt32() != Magic)
                { input.Dispose(); Remove(name); return null; }
                if (input.ReadUInt32() < size) return null;
                var data = input.ReadBytes((int)input.BaseStream.Length - 8);
                var entry = _entries[name];
                var now = DateTime.UtcNow;
                DateTime persisted = entry.Persisted;
                if (now - persisted > TimeSpan.FromMinutes(1))
                {
                    File.SetLastWriteTimeUtc(path, now);
                    persisted = now;
                }
                _entries[name] = (entry.Bytes, now, persisted);
                ct.ThrowIfCancellationRequested();
                return data;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
    }, ct);

    public Task StoreAsync(FileVersion version, uint size, byte[] data, int generation, CancellationToken ct) => Task.Run(() =>
    {
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            if (generation != Generation || data.Length is 0 or > MaxEntryBytes || data.Length + 8 > byteLimit) return;
            string name = Key(version), path = Path.Combine(directory, name), temporary = path + ".tmp";
            try
            {
                Initialize();
                Directory.CreateDirectory(directory);
                // 读取期间被覆盖的原文件不能写入旧版本缓存。
                if (FileVersion.Read(version.Path) != version) return;
                if (File.Exists(path))
                {
                    using var previous = new BinaryReader(File.OpenRead(path));
                    if (previous.BaseStream.Length > 8 && previous.ReadUInt32() == Magic && previous.ReadUInt32() >= size) return;
                }
                using (var output = new BinaryWriter(File.Create(temporary)))
                {
                    output.Write(Magic);
                    output.Write(size);
                    output.Write(data);
                }
                ct.ThrowIfCancellationRequested();
                if (generation != Generation) return;
                File.Move(temporary, path, overwrite: true);
                if (_entries.Remove(name, out var old)) _bytes -= old.Bytes;
                var now = DateTime.UtcNow;
                _entries[name] = (data.Length + 8, now, now);
                _bytes += data.Length + 8;
                Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }, ct);

    public Task RemoveAsync(FileVersion version) => Task.Run(() =>
    {
        lock (_gate) { try { Initialize(); Remove(Key(version)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    });

    public Task ClearAsync()
    {
        Interlocked.Increment(ref _generation);
        return Task.Run(() =>
        {
            lock (_gate)
            {
                Initialize();
                // 只删除本缓存的文件，不递归删除目录或其它应用数据。
                foreach (string name in _entries.Keys.ToArray()) Remove(name);
            }
        });
    }

    private void Initialize()
    {
        if (_initialized) return;
        Directory.CreateDirectory(directory);
        foreach (string abandoned in Directory.EnumerateFiles(directory, "*.thumb.tmp")) File.Delete(abandoned);
        _entries.Clear();
        _bytes = 0;
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.thumb"))
        {
            _entries[file.Name] = (file.Length, file.LastWriteTimeUtc, file.LastWriteTimeUtc);
            _bytes += file.Length;
        }
        Trim();
        _initialized = true;
    }

    private void Trim()
    {
        if (_bytes <= byteLimit && _entries.Count <= countLimit) return;
        foreach (var entry in _entries.OrderBy(pair => pair.Value.Used).ToArray())
        {
            Remove(entry.Key);
            if (_bytes <= byteLimit && _entries.Count <= countLimit) break;
        }
    }

    private void Remove(string name)
    {
        File.Delete(Path.Combine(directory, name));
        if (_entries.Remove(name, out var removed)) _bytes -= removed.Bytes;
    }
}
