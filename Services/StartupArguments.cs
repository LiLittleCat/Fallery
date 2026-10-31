using System.Runtime.InteropServices;

namespace Fallery.Services;

/// <summary>“打开方式”和命令行传入的路径。首个实例读进程参数，重定向的实例只拿到整行命令文本。</summary>
public static class StartupArguments
{
    /// <summary>取第一个存在的文件或目录；忽略 exe 自身和以 - / 开头的开关。</summary>
    public static string? FindPath(IEnumerable<string> args)
    {
        foreach (var raw in args)
        {
            var arg = raw.Trim().Trim('"');
            if (arg.Length == 0 || arg[0] is '-' or '/') continue;
            try
            {
                var path = Path.GetFullPath(arg);
                if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(path) || Directory.Exists(path)) return path;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { }
        }
        return null;
    }

    public static string? FindPath(string? commandLine) =>
        string.IsNullOrWhiteSpace(commandLine) ? null : FindPath(Split(commandLine));

    /// <summary>按 Windows 规则拆分命令行，处理引号、空格和中文路径。</summary>
    public static string[] Split(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out int count);
        if (argv == IntPtr.Zero) return [];
        try
        {
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
            return result;
        }
        finally { LocalFree(argv); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
