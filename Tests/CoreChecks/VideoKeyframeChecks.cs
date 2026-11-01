using Fallery.Services.Playback;
using Windows.Storage;

internal static class VideoKeyframeChecks
{
    public static async Task RunAsync(string path)
    {
        int count = 0;
        void Check(bool value) { if (!value) throw new InvalidOperationException("Keyframe check failed"); count++; }
        var index = new VideoKeyframes([0, 25_000_000, 50_000_000, 75_000_000, 100_000_000]);
        TimeSpan T(double value) => TimeSpan.FromSeconds(value);
        Check(index.Nearest(T(4), T(1), T(12)) == T(5));
        Check(index.Nearest(T(4), T(7), T(12)) == T(5));
        Check(index.Nearest(T(5.2), T(5), T(12)) == T(7.5));
        Check(index.Nearest(T(4.8), T(5), T(12)) == T(2.5));
        Check(index.Nearest(T(0), T(5), T(12)) == T(0));
        Check(index.Nearest(T(12), T(5), T(12)) == T(12));
        Check(new VideoKeyframes([0, 200_000_000]).Nearest(T(10), T(5), T(30)) == T(10));
        Check(await VideoKeyframes.LoadAsync("unsupported.mkv", T(10), CancellationToken.None) is null);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Check(await VideoKeyframes.LoadAsync(path, T(10), cancelled.Token) is null);
        Check(await VideoKeyframes.LoadAsync(path + ".missing.mp4", T(10), CancellationToken.None) is null);
        var file = await StorageFile.GetFileFromPathAsync(path);
        var properties = await file.Properties.GetVideoPropertiesAsync();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var actual = await VideoKeyframes.LoadAsync(path, properties.Duration, CancellationToken.None);
        Console.WriteLine($"Native index available={actual is not null}, elapsed={timer.ElapsedMilliseconds}ms, duration={properties.Duration.TotalSeconds:F2}s");
        Check(actual is not null);
        bool snappedAny = false;
        foreach (double fraction in new[] { .2, .5, .7, .9 })
        {
            var target = properties.Duration * fraction;
            var snapped = actual!.Nearest(target, TimeSpan.Zero, properties.Duration);
            Console.WriteLine($"target={target.TotalSeconds:F3}, keyframe={snapped.TotalSeconds:F3}");
            Check(Math.Abs((snapped - target).TotalSeconds) <= 3 && snapped >= TimeSpan.Zero && snapped <= properties.Duration);
            snappedAny |= snapped != target;
        }
        Check(snappedAny);
        Console.WriteLine($"PASS: {count} keyframe checks (read-only, no playback)");
    }
}
