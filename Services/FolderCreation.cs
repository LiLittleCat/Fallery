using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Fallery.Services;

public static class FolderCreation
{
    public static Task<string> CreateAsync(string parent, string name) => Task.Run(() =>
    {
        ValidateName(name);
        if (!Path.IsPathFullyQualified(parent)) throw new ArgumentException(AppText.Get("OpenTheLocationWhereYouWantToCreateThe"));
        string path = Path.Combine(Path.GetFullPath(parent), name);
        // 原生单目录创建会明确报告重名，也不会在父目录被删除时悄悄重建整条路径。
        if (!CreateDirectory(path, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            if (error is 80 or 183) throw new IOException(AppText.Get("AFileOrFolderWithThisNameAlreadyExists"));
            throw new IOException(AppText.Format("CouldNotCreateFolder", new Win32Exception(error).Message));
        }
        return path;
    });

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(' ') || name.EndsWith('.'))
            throw new ArgumentException(AppText.Get("EnterAValidNameWithoutPathSeparatorsOrTrailing"));
        string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
            && "123456789¹²³".Contains(stem[3]))
            throw new ArgumentException(AppText.Get("ThisIsAReservedWindowsNameChooseAnotherName"));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectory(string path, IntPtr securityAttributes);
}
