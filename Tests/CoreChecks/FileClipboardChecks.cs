using System.Runtime.InteropServices;
using System.Text;
using Fallery.Services;

internal static class FileClipboardChecks
{
    // 创建独立 HGLOBAL 来模拟资源管理器格式，绝不读取/替换用户全局剪贴板。
    private static nint Allocate(byte[] bytes)
    {
        nint memory = GlobalAlloc(0x42, (nuint)bytes.Length);
        if (memory == 0) throw new OutOfMemoryException();
        nint data = GlobalLock(memory);
        try { Marshal.Copy(bytes, 0, data, bytes.Length); }
        finally { GlobalUnlock(memory); }
        return memory;
    }

    private static string[] ReadDrop(string[] paths, bool unicode = true)
    {
        var names = (unicode ? Encoding.Unicode : Encoding.ASCII).GetBytes(string.Join('\0', paths) + "\0\0");
        var bytes = new byte[20 + names.Length];
        BitConverter.GetBytes(20).CopyTo(bytes, 0); // DROPFILES.pFiles
        BitConverter.GetBytes(unicode ? 1 : 0).CopyTo(bytes, 16); // DROPFILES.fWide
        names.CopyTo(bytes, 20);
        nint memory = Allocate(bytes);
        try { return FileClipboard.ReadFileDrop(memory); }
        finally { GlobalFree(memory); }
    }

    internal static async Task RunAsync(string fixture, Action<bool, string> check)
    {
        string source = Directory.CreateDirectory(Path.Combine(fixture, "来源 空格")).FullName;
        string copyTarget = Directory.CreateDirectory(Path.Combine(fixture, "copy")).FullName;
        string moveTarget = Directory.CreateDirectory(Path.Combine(fixture, "move")).FullName;
        string[] files = Enumerable.Range(0, 64).Select(i => Path.Combine(source, $"图片 {i:000}.jpg")).ToArray();
        foreach (string file in files) await File.WriteAllTextAsync(file, "clipboard fixture: " + Path.GetFileName(file));
        string folder = Directory.CreateDirectory(Path.Combine(source, "子文件夹")).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "说明.txt"), "nested file");
        string[] all = [..files, folder];
        var parsed = ReadDrop(all);
        check(parsed.SequenceEqual(all), "原生 CF_HDROP 完整读回 64 个文件与目录，保留中文/空格且不截断为 16 项");
        var ascii = new[] { @"C:\folder one\file.jpg", @"D:\folder two" };
        check(ReadDrop(ascii, unicode: false).SequenceEqual(ascii), "兼容旧式 ANSI CF_HDROP");
        string longPath = @"C:\" + string.Join('\\', Enumerable.Repeat(new string('a', 80), 5)) + "\\photo.jpg";
        check(ReadDrop([longPath]).Single() == longPath, "原生长路径读取不能使用 MAX_PATH 截断");
        foreach (uint effect in new uint[] { 0, 1, 2, 3, 4, 0x80000002 })
        {
            nint memory = Allocate(BitConverter.GetBytes(effect));
            try
            {
                uint? actual = FileClipboard.ReadPreferredEffect(memory);
                check(actual == effect && FileClipboard.RequestsMove(actual) == (effect is 2 or 0x80000002),
                    "原生 Preferred DropEffect 区分复制、剪切与允许多种操作");
            }
            finally { GlobalFree(memory); }
        }
        check(!FileClipboard.RequestsMove(FileClipboard.ReadPreferredEffect(0)), "无剪切标记默认复制");
        nint shortData = GlobalAlloc(0x42, 0);
        try { check(FileClipboard.ReadPreferredEffect(shortData) is null, "空操作标记不能误判为剪切"); }
        finally { GlobalFree(shortData); }
        Task<FileConflictChoice> Conflict(FileConflict _) => Task.FromResult(FileConflictChoice.Skip);
        var copy = await FilePaste.TransferAsync(parsed, copyTarget, false, Conflict, default);
        check(copy.Copied.Count == all.Length && copy.Errors.Count == 0 && all.All(path => File.Exists(path) || Directory.Exists(path)),
            "资源管理器复制列表批量粘贴完成且来源保留");
        check(await File.ReadAllTextAsync(Path.Combine(copyTarget, "子文件夹", "说明.txt")) == "nested file", "复制目录保留内部非媒体文件");
        var moveContent = new FileClipboardContent(ReadDrop(all), true, 42);
        var moved = await FilePaste.TransferAsync(moveContent.Paths, moveTarget, moveContent.Move, Conflict, default);
        check(moved.Copied.Count == all.Length && moved.Errors.Count == 0 && all.All(path => !File.Exists(path) && !Directory.Exists(path)),
            "资源管理器剪切列表批量移动后移除原路径");
        check(await File.ReadAllTextAsync(Path.Combine(moveTarget, "图片 000.jpg")) == "clipboard fixture: 图片 000.jpg",
            "剪切粘贴保留真实文件内容");
        check(FileClipboard.CanCompleteMove(moveContent, moved, 42), "整批剪切成功且剪贴板未变化时可完成清理");
        check(!FileClipboard.CanCompleteMove(moveContent, moved, 43), "用户已复制其他内容时不得清理新剪贴板");
        check(!FileClipboard.CanCompleteMove(moveContent with { Move = false }, moved, 42), "复制完成保留剪贴板用于再次粘贴");
        check(!FileClipboard.CanCompleteMove(moveContent, moved with { Skipped = 1 }, 42)
            && !FileClipboard.CanCompleteMove(moveContent, moved with { Canceled = true }, 42)
            && !FileClipboard.CanCompleteMove(moveContent, new(moved.Copied, ["error"]), 42)
            && !FileClipboard.CanCompleteMove(moveContent, new(moved.Copied.Take(1).ToArray(), []), 42),
            "跳过、取消、失败或仅完成部分项目时保留剪切剪贴板");
    }

    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
}
