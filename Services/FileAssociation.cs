using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Fallery.Services;

/// <summary>
/// 非打包程序把自己注册到当前用户的“打开方式”和“默认应用”列表，不需要管理员权限。
/// Windows 10 起程序不能把自己设成默认应用，只能由用户在“打开方式”或系统设置里选择。
/// </summary>
public static class FileAssociation
{
    public const string ProgId = AppIdentity.Name + ".Media";
    public const string ContextVerb = AppIdentity.Name + ".Open";
    private const string AppKey = AppIdentity.Name + ".exe";
    private const string CapabilitiesPath = @"Software\" + AppIdentity.Name + @"\Capabilities";

    /// <summary>注册当前 exe；softwareRoot 默认是 HKCU\Software，测试时可传入隔离的子键。</summary>
    public static void Register(string exePath, IEnumerable<string> extensions, RegistryKey? softwareRoot = null)
    {
        if (softwareRoot is null && AppIdentity.IsDevelopment) throw new InvalidOperationException("开发版不注册系统打开方式，请使用安装版。");
        using var software = softwareRoot is null ? Registry.CurrentUser.CreateSubKey("Software") : null;
        var root = softwareRoot ?? software!;
        exePath = Path.GetFullPath(exePath);
        string command = $"\"{exePath}\" \"%1\"";
        string icon = $"\"{exePath}\",0";
        var list = extensions.Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()).Distinct().ToList();

        using (var app = root.CreateSubKey($@"Classes\Applications\{AppKey}"))
        {
            app.SetValue("FriendlyAppName", AppIdentity.DisplayName);
            using (var open = app.CreateSubKey(@"shell\open\command")) open.SetValue("", command);
            using var types = app.CreateSubKey("SupportedTypes");
            foreach (var extension in list) types.SetValue(extension, "");
        }
        using (var prog = root.CreateSubKey($@"Classes\{ProgId}"))
        {
            prog.SetValue("", "Fallery 媒体文件");
            using (var defaultIcon = prog.CreateSubKey("DefaultIcon")) defaultIcon.SetValue("", icon);
            using var open = prog.CreateSubKey(@"shell\open\command");
            open.SetValue("", command);
        }
        foreach (var extension in list)
        {
            using var progIds = root.CreateSubKey($@"Classes\{extension}\OpenWithProgids");
            progIds.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
        using (var capabilities = root.CreateSubKey(CapabilitiesPath.Replace(@"Software\", "")))
        {
            capabilities.SetValue("ApplicationName", AppIdentity.DisplayName);
            capabilities.SetValue("ApplicationDescription", "瀑布流图片浏览器和视频播放器");
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var extension in list) associations.SetValue(extension, ProgId);
        }
        using (var registered = root.CreateSubKey("RegisteredApplications")) registered.SetValue(AppIdentity.Name, CapabilitiesPath);
        foreach (var extension in list)
            RegisterVerb(root, $@"SystemFileAssociations\{extension}", exePath, "%1");
        // 尾部加 \.，避免磁盘根目录的反斜杠与命令行结束引号发生转义。
        RegisterVerb(root, "Directory", exePath, @"%1\.");
        RegisterVerb(root, @"Directory\Background", exePath, @"%V\.");
        RegisterVerb(root, "Drive", exePath, @"%1\.");
        if (softwareRoot is null) NotifyShell();
    }

    private static void RegisterVerb(RegistryKey root, string target, string exePath, string argument)
    {
        using var verb = root.CreateSubKey($@"Classes\{target}\shell\{ContextVerb}");
        verb.SetValue("", $"用 {AppIdentity.DisplayName} 打开");
        verb.SetValue("Icon", $"\"{exePath}\",0");
        verb.SetValue("MultiSelectModel", "Single");
        using var command = verb.CreateSubKey("command");
        command.SetValue("", $"\"{exePath}\" \"{argument}\"");
    }

    public static bool IsContextMenuRegistered(string exePath, IEnumerable<string> extensions, RegistryKey? softwareRoot = null)
    {
        using var software = softwareRoot is null ? Registry.CurrentUser.OpenSubKey("Software") : null;
        var root = softwareRoot ?? software;
        var targets = extensions.Select(e => $@"SystemFileAssociations\{(e.StartsWith('.') ? e : "." + e).ToLowerInvariant()}")
            .Select(target => (Target: target, Argument: "%1"))
            .Concat(new[] { ("Directory", @"%1\."), (@"Directory\Background", @"%V\."), ("Drive", @"%1\.") });
        foreach (var (target, argument) in targets)
        {
            using var command = root?.OpenSubKey($@"Classes\{target}\shell\{ContextVerb}\command");
            if (!string.Equals(command?.GetValue("") as string, $"\"{Path.GetFullPath(exePath)}\" \"{argument}\"", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public static void Unregister(IEnumerable<string> extensions, RegistryKey? softwareRoot = null, string? expectedExePath = null)
    {
        if (softwareRoot is null && AppIdentity.IsDevelopment) throw new InvalidOperationException("开发版不修改系统打开方式，请使用安装版。");
        using var software = softwareRoot is null ? Registry.CurrentUser.OpenSubKey("Software", writable: true) : null;
        var root = softwareRoot ?? software;
        if (root is null) return;
        // 安装版升级/迁移后，旧副本卸载不能删掉指向新安装位置的注册。
        if (expectedExePath is not null && RegisteredExecutable(root) is { } registeredExe
            && !string.Equals(Path.GetFullPath(registeredExe), Path.GetFullPath(expectedExePath), StringComparison.OrdinalIgnoreCase)) return;
        using var supported = root.OpenSubKey($@"Classes\Applications\{AppKey}\SupportedTypes");
        var list = extensions.Concat(supported?.GetValueNames() ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        root.DeleteSubKeyTree($@"Classes\Applications\{AppKey}", throwOnMissingSubKey: false);
        root.DeleteSubKeyTree($@"Classes\{ProgId}", throwOnMissingSubKey: false);
        root.DeleteSubKeyTree(AppIdentity.Name + @"\Capabilities", throwOnMissingSubKey: false);
        using (var registered = root.OpenSubKey("RegisteredApplications", writable: true)) registered?.DeleteValue(AppIdentity.Name, throwOnMissingValue: false);
        foreach (var extension in list)
        {
            var name = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
            using var progIds = root.OpenSubKey($@"Classes\{name}\OpenWithProgids", writable: true);
            progIds?.DeleteValue(ProgId, throwOnMissingValue: false);
            root.DeleteSubKeyTree($@"Classes\SystemFileAssociations\{name}\shell\{ContextVerb}", throwOnMissingSubKey: false);
        }
        foreach (var target in new[] { "Directory", @"Directory\Background", "Drive" })
            root.DeleteSubKeyTree($@"Classes\{target}\shell\{ContextVerb}", throwOnMissingSubKey: false);
        if (softwareRoot is null) NotifyShell();
    }

    /// <summary>注册指向的 exe；未注册返回 null。用于提示“程序移动后需要重新注册”。</summary>
    public static string? RegisteredExecutable(RegistryKey? softwareRoot = null)
    {
        using var software = softwareRoot is null ? Registry.CurrentUser.OpenSubKey("Software") : null;
        var root = softwareRoot ?? software;
        using var command = root?.OpenSubKey($@"Classes\Applications\{AppKey}\shell\open\command");
        if (command?.GetValue("") is not string value) return null;
        var args = StartupArguments.Split(value);
        return args.Length > 0 ? args[0] : null;
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
