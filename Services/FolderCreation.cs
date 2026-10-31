using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Fallery.Services;

public static class FolderCreation
{
    public static Task<string> CreateAsync(string parent, string name) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(' ') || name.EndsWith('.'))
            throw new ArgumentException("请输入有效的文件夹名称，不能包含路径分隔符，也不能以空格或句点结尾。");
        string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
            && "123456789¹²³".Contains(stem[3]))
            throw new ArgumentException("此名称是 Windows 保留名称，请换一个名称。");
        if (!Path.IsPathFullyQualified(parent)) throw new ArgumentException("请先打开要创建文件夹的位置。");
        string path = Path.Combine(Path.GetFullPath(parent), name);
        // 原生单目录创建会明确报告重名，也不会在父目录被删除时悄悄重建整条路径。
        if (!CreateDirectory(path, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            if (error is 80 or 183) throw new IOException("已存在同名文件或文件夹，请换一个名称。");
            throw new IOException($"无法创建文件夹：{new Win32Exception(error).Message}");
        }
        return path;
    });

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectory(string path, IntPtr securityAttributes);
}
