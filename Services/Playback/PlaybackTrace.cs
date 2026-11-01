using System.Threading.Channels;

namespace Fallery.Services.Playback;

/// <summary>按需启用的播放诊断；后台写入，不记录文件路径或媒体内容。</summary>
internal static class PlaybackTrace
{
    private static readonly Channel<string>? Lines = CreateWriter();
    internal static bool Enabled => Lines is not null;

    internal static void Write(string message) => Lines?.Writer.TryWrite($"{DateTimeOffset.Now:O} {message}");

    private static Channel<string>? CreateWriter()
    {
        string? path = Environment.GetEnvironmentVariable("FALLERY_PLAYBACK_TRACE");
        if (string.IsNullOrWhiteSpace(path)) return null;
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1024)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
        _ = Task.Run(async () =>
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
                await using var writer = new StreamWriter(stream) { AutoFlush = true };
                await foreach (string line in channel.Reader.ReadAllAsync()) await writer.WriteLineAsync(line);
            }
            catch (Exception) { /* 诊断失败不能影响正常播放。 */ }
        });
        return channel;
    }
}
