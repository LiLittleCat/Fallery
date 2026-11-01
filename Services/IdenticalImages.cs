using System.Diagnostics;
using System.Security.Cryptography;
using Fallery.Models;

namespace Fallery.Services;

public enum IdenticalImageMatchMode { NameSizeContent, NameOnly }

public sealed record IdenticalImageMatch(string Path, FileVersion Version, string Hash, string SamplePath)
{
    public IdenticalImageMatchMode Mode { get; init; } = IdenticalImageMatchMode.NameSizeContent;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Parent => System.IO.Path.GetDirectoryName(Path) ?? Path;
    public string SampleName => System.IO.Path.GetFileName(SamplePath);
}
public sealed record IdenticalImageScan(IReadOnlyList<IdenticalImageMatch> Matches, int Examined, int Skipped, bool Limited);

// 默认同时比较名称、大小和内容；仅名称模式也记录候选摘要，用于删除前检测扫描后的变化。
public static class IdenticalImages
{
    public const int MatchLimit = 20_000;
    public const int SampleLimit = 64;

    public static Task<IdenticalImageScan> ScanAsync(string root, IReadOnlyList<string> samples, bool showHidden,
        IProgress<string>? progress, CancellationToken ct, int matchLimit = MatchLimit,
        IdenticalImageMatchMode mode = IdenticalImageMatchMode.NameSizeContent) => Task.Run(async () =>
    {
        root = Path.GetFullPath(root);
        ValidatePath(root, root);
        if (samples.Count is 0 or > SampleLimit) throw new ArgumentException($"请选择 1–{SampleLimit} 张图片作为样本。");
        if (matchLimit < 1) throw new ArgumentOutOfRangeException(nameof(matchLimit));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var samplesByName = new Dictionary<string, List<(string Path, long Length, string? Hash)>>(StringComparer.OrdinalIgnoreCase);
        foreach (string sample in samples.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            ValidatePath(root, sample);
            if (FolderScanner.GetMediaKind(sample) != MediaKind.Image) throw new ArgumentException("样本只能包含图片。");
            var version = FileVersion.Read(sample) ?? throw new IOException("样本不可读取。");
            string? hash = null;
            if (mode == IdenticalImageMatchMode.NameSizeContent)
            {
                var fingerprint = await FingerprintAsync(sample, ct);
                version = fingerprint.Version;
                hash = fingerprint.Hash;
            }
            string name = Path.GetFileName(sample);
            if (!samplesByName.TryGetValue(name, out var group)) samplesByName.Add(name, group = []);
            group.Add((sample, version.Length, hash));
        }
        var matches = new List<IdenticalImageMatch>();
        int examined = 0, skipped = 0;
        bool limited = false;
        var pending = new Stack<string>(); pending.Push(root);
        var clock = Stopwatch.StartNew();
        while (pending.TryPop(out string? directory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ValidatePath(root, directory);
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", new EnumerationOptions
                { IgnoreInaccessible = false, AttributesToSkip = FolderScanner.TraversalSkip(showHidden) }))
                {
                    ct.ThrowIfCancellationRequested();
                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        if (!FolderScanner.IsExcludedDirectory(entry.FullName)) pending.Push(entry.FullName);
                        continue;
                    }
                    if (FolderScanner.GetMediaKind(entry.FullName) != MediaKind.Image) continue;
                    examined++;
                    try
                    {
                        if (entry is FileInfo info && samplesByName.TryGetValue(info.Name, out var candidates)
                            && (mode == IdenticalImageMatchMode.NameOnly || candidates.Any(sample => sample.Length == info.Length)))
                        {
                            ValidatePath(root, entry.FullName);
                            var fingerprint = await FingerprintAsync(entry.FullName, ct);
                            string? sample = mode == IdenticalImageMatchMode.NameOnly ? candidates[0].Path
                                : candidates.FirstOrDefault(candidate => candidate.Length == fingerprint.Version.Length
                                    && candidate.Hash == fingerprint.Hash).Path;
                            if (sample is not null)
                            {
                                matches.Add(new(entry.FullName, fingerprint.Version, fingerprint.Hash, sample) { Mode = mode });
                                if (matches.Count >= matchLimit) { limited = true; break; }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                    if (clock.ElapsedMilliseconds >= 150)
                    { progress?.Report($"已检查 {examined:N0} 张图片，找到 {matches.Count:N0} 项"); clock.Restart(); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
            if (limited) break;
        }
        return new IdenticalImageScan(matches.OrderBy(match => match.Path, StringComparer.OrdinalIgnoreCase).ToArray(), examined, skipped, limited);
    }, ct);

    public static Task<bool> IsUnchangedAsync(string root, IdenticalImageMatch match, CancellationToken ct) => Task.Run(async () =>
    {
        try
        {
            ValidatePath(root, match.Path);
            if (FileVersion.Read(match.Path) != match.Version) return false;
            var current = await FingerprintAsync(match.Path, ct);
            return current.Version == match.Version && current.Hash == match.Hash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }, ct);

    private static async Task<(FileVersion Version, string Hash)> FingerprintAsync(string path, CancellationToken ct)
    {
        var before = FileVersion.Read(path) ?? throw new IOException("文件不可读取。");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        if (FileVersion.Read(path) != before) throw new IOException("文件在读取期间发生变化。");
        return (before, hash);
    }

    private static void ValidatePath(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!path.Equals(root, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("文件不在本次扫描范围内。");
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("不扫描联接或符号链接，请打开实际目录。");
    }
}
