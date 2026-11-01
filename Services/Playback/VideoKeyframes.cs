using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Fallery.Services.Playback;

/// <summary>只读取系统 MP4 源的关键帧索引，不解码、播放或依赖外部程序。</summary>
internal sealed class VideoKeyframes(long[] ticks)
{
    private static readonly SemaphoreSlim Readers = new(2);

    public TimeSpan Nearest(TimeSpan target, TimeSpan current, TimeSpan duration)
    {
        if (ticks.Length == 0 || target <= TimeSpan.Zero || target >= duration) return target;
        int index = Array.BinarySearch(ticks, target.Ticks);
        if (index >= 0) return target;
        index = ~index;
        long best = target.Ticks, distance = long.MaxValue;
        foreach (int i in new[] { index - 1, index })
        {
            if (i < 0 || i >= ticks.Length) continue;
            long candidate = ticks[i];
            // 短跳转不能吸附回原位置或反方向；偏差过大时保留准确定位。
            if (target > current ? candidate <= current.Ticks : candidate >= current.Ticks) continue;
            long delta = Math.Abs(candidate - target.Ticks);
            if (delta <= TimeSpan.TicksPerSecond * 3 && delta < distance)
            { best = candidate; distance = delta; }
        }
        return TimeSpan.FromTicks(best);
    }

    public static async Task<VideoKeyframes?> LoadAsync(string path, TimeSpan duration, CancellationToken ct)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is not (".mp4" or ".m4v" or ".mov" or ".3gp" or ".3g2")) return null;
        try
        {
            await Readers.WaitAsync(ct).ConfigureAwait(false);
            try { return await Task.Run(() => Read(path, duration, ct), ct).ConfigureAwait(false); }
            finally { Readers.Release(); }
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
        { return null; }
    }

    private static VideoKeyframes? Read(string path, TimeSpan duration, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (MFStartup(0x20070, 0) < 0) return null;
        ISourceResolver? resolver = null;
        object? source = null;
        nint service = 0;
        try
        {
            Marshal.ThrowExceptionForHR(MFCreateSourceResolver(out resolver));
            // MF_RESOLUTION_MEDIASOURCE | MF_RESOLUTION_READ
            Marshal.ThrowExceptionForHR(resolver.CreateObjectFromURL(path, 0x10001, 0, out _, out source));
            ct.ThrowIfCancellationRequested();
            var scrub = new Guid("DD0AC3D8-40E3-4128-AC48-C0ADD067B714");
            var iid = new Guid("26AFEA53-D9ED-42B5-AB80-E64F9EE34779");
            if (MFGetService(source, ref scrub, ref iid, out service) < 0) return null;
            // 服务返回的接口可能没有经源对象 IUnknown 再次暴露，直接使用返回的接口指针。
            var seek = Marshal.GetDelegateForFunctionPointer<GetNearestKeyFrames>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(service), 3 * IntPtr.Size));
            var result = new SortedSet<long>();
            var timer = Stopwatch.StartNew();
            long position = 0;
            while (position < duration.Ticks && result.Count < 100_000 && timer.Elapsed < TimeSpan.FromSeconds(2))
            {
                ct.ThrowIfCancellationRequested();
                var format = Guid.Empty;
                var input = new PropVariant { Type = 20, Value = position }; // VT_I8, 100ns
                PropVariant previous = default, next = default;
                try
                {
                    if (seek(service, ref format, ref input, out previous, out next) < 0) return null;
                    if (previous.Type is 20 or 21 && previous.Value >= 0 && previous.Value < duration.Ticks)
                        result.Add(previous.Value);
                    if (next.Type is 20 or 21 && next.Value >= 0 && next.Value < duration.Ticks)
                        result.Add(next.Value);
                    // 源可能把关键帧后的一小段时间仍映射为同一帧；继续越过该帧，不能误判为 EOF。
                    // 密集 I 帧最多间隔 100ms 采样，所有保留点仍来自系统索引。
                    if (next.Type is 20 or 21 && next.Value > position)
                        position = Math.Min(next.Value, duration.Ticks);
                    position += Math.Min(duration.Ticks - position, TimeSpan.TicksPerMillisecond * 100);
                }
                finally { PropVariantClear(ref previous); PropVariantClear(ref next); }
            }
            // 不发布截断索引，以免误选远处关键帧。
            return position >= duration.Ticks && result.Count > 0 ? new(result.ToArray()) : null;
        }
        finally
        {
            if (service != 0) Marshal.Release(service);
            if (source is IMediaSource mediaSource) mediaSource.Shutdown();
            if (source is not null) Marshal.ReleaseComObject(source);
            if (resolver is not null) Marshal.ReleaseComObject(resolver);
            MFShutdown();
        }
    }

    // PROPVARIANT 在 x64/ARM64 均为 24 字节；输出必须 PropVariantClear。
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public long Value;
    }

    [ComImport, Guid("FBE5A32D-A497-4B61-BB85-97B1A848A6E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISourceResolver
    {
        [PreserveSig] int CreateObjectFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, uint flags,
            nint properties, out int type, [MarshalAs(UnmanagedType.IUnknown)] out object source);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetNearestKeyFrames(nint self, ref Guid format, ref PropVariant target,
        out PropVariant previous, out PropVariant next);

    [ComImport, Guid("279A808D-AEC7-40C8-9C6B-A6B492C78A66"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMediaSource
    {
        [PreserveSig] int GetEvent(uint flags, out nint value);
        [PreserveSig] int BeginGetEvent(nint callback, nint state);
        [PreserveSig] int EndGetEvent(nint result, out nint value);
        [PreserveSig] int QueueEvent(uint type, ref Guid extendedType, int status, nint value);
        [PreserveSig] int GetCharacteristics(out uint value);
        [PreserveSig] int CreatePresentationDescriptor(out nint descriptor);
        [PreserveSig] int Start(nint descriptor, ref Guid format, nint position);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Shutdown();
    }

    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFShutdown();
    [DllImport("mf.dll", ExactSpelling = true)] private static extern int MFCreateSourceResolver(out ISourceResolver resolver);
    [DllImport("mf.dll", ExactSpelling = true)] private static extern int MFGetService(
        [MarshalAs(UnmanagedType.IUnknown)] object source, ref Guid service, ref Guid iid,
        out nint value);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int PropVariantClear(ref PropVariant value);
}
