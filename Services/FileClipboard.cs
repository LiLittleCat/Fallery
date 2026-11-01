using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Windows.ApplicationModel.DataTransfer;

namespace Fallery.Services;

public sealed record FileClipboardContent(IReadOnlyList<string> Paths, bool Move, uint Sequence);

/// <summary>资源管理器优先读取原生 HDROP，其他应用回退 StorageItems；不修改剪贴板来转换格式。</summary>
public static class FileClipboard
{
    private const uint FileDropFormat = 15;
    private static readonly uint PreferredEffectFormat = RegisterClipboardFormat("Preferred DropEffect");

    public static bool HasFiles()
    {
        if (IsClipboardFormatAvailable(FileDropFormat)) return true;
        try { return Clipboard.GetContent().Contains(StandardDataFormats.StorageItems); }
        catch (Exception) { return false; }
    }

    public static async Task<FileClipboardContent?> ReadAsync(nint owner, CancellationToken ct)
    {
        if (IsClipboardFormatAvailable(FileDropFormat))
        {
            // 剪贴板可能暂时被资源管理器占用；异步短暂重试，不阻塞界面。
            for (int attempt = 0; attempt < 10; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (OpenClipboard(owner))
                {
                    FileClipboardContent? snapshot = null;
                    uint? effect = null;
                    try
                    {
                        if (IsClipboardFormatAvailable(FileDropFormat))
                        {
                            nint files = GetClipboardData(FileDropFormat);
                            if (files == 0) throw new IOException("无法读取剪贴板文件列表。", new Win32Exception(Marshal.GetLastWin32Error()));
                            var paths = ReadFileDrop(files);
                            effect = ReadPreferredEffect(GetClipboardData(PreferredEffectFormat));
                            // 在延迟渲染完成后记录代次；列表与剪切标记在同一次锁定中读取。
                            snapshot = new(paths, RequestsMove(effect), GetClipboardSequenceNumber());
                        }
                    }
                    finally { CloseClipboard(); }
                    if (snapshot is not null)
                    {
                        // 某些 WinRT 来源有 HDROP，但只在 RequestedOperation 中记录剪切。
                        // 释放原生锁后再访问 WinRT，避免内部重新打开剪贴板时互相阻塞。
                        if (effect is null)
                        {
                            try
                            {
                                var operation = Clipboard.GetContent().RequestedOperation;
                                if (snapshot.Sequence != GetClipboardSequenceNumber()) throw new IOException("剪贴板内容已改变，请重新粘贴。");
                                snapshot = snapshot with { Move = RequestsMove((uint)operation) };
                            }
                            catch (System.Runtime.InteropServices.COMException) { }
                        }
                        return snapshot;
                    }
                    break;
                }
                if (attempt == 9) throw new IOException("剪贴板正被其他程序使用，请稍后重试粘贴。");
                await Task.Delay(30, ct);
            }
        }

        ct.ThrowIfCancellationRequested();
        uint sequence = GetClipboardSequenceNumber();
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.StorageItems)) return null;
        var items = await content.GetStorageItemsAsync().AsTask(ct);
        var pathsFallback = items.Select(item => item.Path).ToArray();
        if (pathsFallback.Any(string.IsNullOrWhiteSpace))
            throw new IOException("部分剪贴板项目没有本地文件路径，请先在资源管理器中保存到磁盘。");
        if (sequence != GetClipboardSequenceNumber()) throw new IOException("剪贴板内容已改变，请重新粘贴。");
        return new(pathsFallback, RequestsMove((uint)content.RequestedOperation), sequence);
    }

    internal static string[] ReadFileDrop(nint files)
    {
        uint count = DragQueryFile(files, uint.MaxValue, null, 0);
        if (count > 100_000) throw new IOException("剪贴板项目过多，请分批粘贴。");
        var paths = new string[checked((int)count)];
        for (uint i = 0; i < count; i++)
        {
            uint length = DragQueryFile(files, i, null, 0);
            if (length is 0 or > 32767) throw new IOException("剪贴板包含无效文件路径。");
            var path = new StringBuilder(checked((int)length + 1));
            if (DragQueryFile(files, i, path, (uint)path.Capacity) != length)
                throw new IOException("剪贴板文件列表读取不完整，请重新复制。");
            paths[i] = path.ToString();
        }
        return paths;
    }

    internal static uint? ReadPreferredEffect(nint memory)
    {
        if (memory == 0 || GlobalSize(memory) < 4) return null;
        nint data = GlobalLock(memory);
        if (data == 0) return null;
        try { return unchecked((uint)Marshal.ReadInt32(data)); }
        finally { GlobalUnlock(memory); }
    }

    // DWORD DROPEFFECT；允许多种操作或没有明确剪切标记时优先复制，不擅自删除来源。
    internal static bool RequestsMove(uint? effect) => effect is { } value && (value & 2) != 0 && (value & 1) == 0;

    public static bool CompleteMove(FileClipboardContent content, FilePasteResult result, nint owner)
    {
        if (!CanCompleteMove(content, result, GetClipboardSequenceNumber()) || !OpenClipboard(owner)) return false;
        try
        {
            // 锁内再次核对，不能清除粘贴过程中用户新复制的内容。文件已由 TransferAsync 移动，
            // 不再发送可能要求来源额外删除的 delete-on-paste 回调；释放剪切剪贴板即可。
            return CanCompleteMove(content, result, GetClipboardSequenceNumber()) && EmptyClipboard();
        }
        finally { CloseClipboard(); }
    }

    internal static bool CanCompleteMove(FileClipboardContent content, FilePasteResult result, uint sequence) =>
        content.Move && content.Sequence != 0 && content.Sequence == sequence && !result.Canceled
        && result.Skipped == 0 && result.Errors.Count == 0 && result.Copied.Count > 0
        && result.Copied.Count == content.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string format);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(nint files, uint index, StringBuilder? path, uint length);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
}
