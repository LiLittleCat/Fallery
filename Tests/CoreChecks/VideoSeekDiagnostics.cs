using System.Diagnostics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

internal static class VideoSeekDiagnostics
{
    internal static async Task RunAsync(string path)
    {
        using var player = new MediaPlayer { AutoPlay = false, IsMuted = true, Volume = 0 };
        player.RealTimePlayback = Environment.GetEnvironmentVariable("FALLERY_SEEK_REALTIME") == "1";
        player.CommandManager.IsEnabled = false;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaOpened += (_, _) => opened.TrySetResult();
        player.MediaFailed += (_, e) => opened.TrySetException(new IOException(e.ErrorMessage));
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var source = MediaSource.CreateFromStorageFile(file);
        player.Source = source;
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var session = player.PlaybackSession;
        Console.WriteLine($"Seek sample: {session.NaturalVideoWidth}x{session.NaturalVideoHeight}, duration={session.NaturalDuration.TotalSeconds:F2}s, realtime={player.RealTimePlayback}, muted");
        if (Environment.GetEnvironmentVariable("FALLERY_SEEK_PLAYING") == "1")
        {
            player.Play();
            await Task.Delay(250);
        }
        var elapsed = Stopwatch.StartNew();
        TaskCompletionSource? completion = null;
        session.SeekCompleted += (_, _) => completion?.TrySetResult();
        foreach (double fraction in new[] { 0.2, 0.5, 0.3 })
        {
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            double target = session.NaturalDuration.TotalSeconds * fraction;
            elapsed.Restart();
            session.Position = TimeSpan.FromSeconds(target);
            double setter = elapsed.Elapsed.TotalMilliseconds;
            double immediate = session.Position.TotalSeconds;
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
            double completed = elapsed.Elapsed.TotalMilliseconds;
            if (Environment.GetEnvironmentVariable("FALLERY_SEEK_PLAYING") == "1")
            {
                while (session.Position.TotalSeconds < target + 0.1 && elapsed.ElapsedMilliseconds < 8000)
                    await Task.Delay(10);
            }
            Console.WriteLine($"Seek {target:F3}s: setter={setter:F1}ms, immediate={immediate:F3}s, completed={completed:F1}ms, advanced100ms={elapsed.Elapsed.TotalMilliseconds:F1}ms, actual={session.Position.TotalSeconds:F3}s, state={session.PlaybackState}");
        }
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        double start = session.Position.TotalSeconds;
        double step = session.NaturalDuration.TotalSeconds / 20;
        elapsed.Restart();
        for (int i = 0; i < 5; i++) session.Position += TimeSpan.FromSeconds(step);
        Console.WriteLine($"Burst expected={start + 5 * step:F3}s, immediate={session.Position.TotalSeconds:F3}s, setters={elapsed.Elapsed.TotalMilliseconds:F1}ms");
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Task.Delay(500);
        Console.WriteLine($"Burst settled={session.Position.TotalSeconds:F3}s");
        player.Source = null;
    }
}
