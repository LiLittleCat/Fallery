using Microsoft.Win32;

namespace Fallery.Services;

/// <summary>安装器与便携版共用的无窗口入口，必须在 WinUI / 单实例初始化前处理。</summary>
public static class ShellRegistrationCommand
{
    public static bool TryExecute(string[] args, string exePath, IEnumerable<string> extensions,
        out int exitCode, RegistryKey? softwareRoot = null)
    {
        exitCode = 0;
        bool IsSwitch(string arg) => arg is "--register-shell" or "--unregister-shell";
        if (!args.Any(IsSwitch)) return false;
        if (args.Length != 1) { exitCode = 2; return true; }
        try
        {
            if (args[0] == "--register-shell") FileAssociation.Register(exePath, extensions, softwareRoot);
            else FileAssociation.Unregister(extensions, softwareRoot, expectedExePath: exePath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or ArgumentException)
        {
            exitCode = 1;
        }
        return true;
    }
}
