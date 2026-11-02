using Fallery.Services;
using Microsoft.Win32;

internal static class ShellRegistrationChecks
{
    internal static void Run(string fixture, Action<bool, string> check)
    {
        string key = $@"Software\FalleryCoreChecks\Shell-{Guid.NewGuid():N}";
        using var root = Registry.CurrentUser.CreateSubKey(key);
        const string firstExe = @"C:\Apps\媒体工具 便携版\Fallery.exe";
        const string installedExe = @"C:\Program Files\Fallery\Fallery.exe";
        string[] extensions = ["JPG", ".mp4"];
        string[] targets = [@"SystemFileAssociations\.jpg", @"SystemFileAssociations\.mp4", "Directory", @"Directory\Background", "Drive"];
        string? Read(string path, string name = "")
        { using var item = root.OpenSubKey(path); return item?.GetValue(name) as string; }
        try
        {
            using (var foreign = root.CreateSubKey(@"Classes\Directory\shell\OtherApp")) foreign.SetValue("", "keep");
            using (var settings = root.CreateSubKey(@"Fallery\OtherSettings")) settings.SetValue("", "keep");
            using (var defaultApp = root.CreateSubKey(@"Classes\.jpg")) defaultApp.SetValue("", "OtherApp.Image");
            check(!FileAssociation.IsContextMenuRegistered(firstExe, extensions, root), "未注册时不能显示右键入口已就绪");
            check(!ShellRegistrationCommand.TryExecute([fixture], firstExe, extensions, out _, root), "普通目录参数继续走应用启动");
            check(ShellRegistrationCommand.TryExecute(["--register-shell", fixture], firstExe, extensions, out int invalid, root)
                && invalid == 2 && FileAssociation.RegisteredExecutable(root) is null, "安装命令拒绝混入文件参数且不能写注册表");
            check(ShellRegistrationCommand.TryExecute(["--register-shell"], firstExe, extensions, out int registered, root) && registered == 0,
                "无窗口注册命令成功返回 0");
            check(FileAssociation.IsContextMenuRegistered(firstExe, extensions, root), "注册同时包含文件、文件夹、空白处和磁盘入口");
            string spacedFolder = Directory.CreateDirectory(Path.Combine(fixture, "右键 空格目录")).FullName;
            string spacedFile = Path.Combine(spacedFolder, "照片 空格.jpg");
            File.WriteAllText(spacedFile, "fixture");
            foreach (string target in targets)
            {
                string verb = $@"Classes\{target}\shell\{FileAssociation.ContextVerb}";
                bool file = target.StartsWith("SystemFileAssociations");
                string actual = target == "Drive" ? Path.GetPathRoot(fixture)! : file ? spacedFile : spacedFolder;
                string command = Read(verb + @"\command")!;
                string expanded = command.Replace("%1", actual).Replace("%V", actual);
                check(StartupArguments.Split(expanded)[0] == firstExe, "右键命令完整保留含空格及中文的程序路径");
                check(StartupArguments.FindPath(expanded) == actual, "文件、目录、空白处和磁盘根参数按 Windows 规则解析");
                check(Read(verb, "Icon") == $"\"{firstExe}\",0" && Read(verb, "MultiSelectModel") == "Single",
                    "菜单使用当前 exe 图标且不对多选逐个打开");
            }
            check(Read(@"Classes\.jpg") == "OtherApp.Image", "注册不得改变现有默认应用");
            root.DeleteSubKeyTree($@"Classes\Directory\shell\{FileAssociation.ContextVerb}");
            check(!FileAssociation.IsContextMenuRegistered(firstExe, extensions, root), "部分注册缺失应提示补全");
            ShellRegistrationCommand.TryExecute(["--register-shell"], installedExe, extensions, out _, root);
            check(FileAssociation.IsContextMenuRegistered(installedExe, extensions, root)
                && FileAssociation.RegisteredExecutable(root) == installedExe, "迁移到安装版后所有入口更新到安装目录");
            ShellRegistrationCommand.TryExecute(["--unregister-shell"], firstExe, extensions, out _, root);
            check(FileAssociation.IsContextMenuRegistered(installedExe, extensions, root), "旧副本卸载不能清理新安装位置的入口");
            // 卸载时扩展名列表缩短，也清理之前版本在 SupportedTypes 中记录的入口。
            check(ShellRegistrationCommand.TryExecute(["--unregister-shell"], installedExe, [".jpg"], out int removed, root) && removed == 0,
                "无窗口卸载命令成功返回 0");
            foreach (string target in targets)
            {
                using var verb = root.OpenSubKey($@"Classes\{target}\shell\{FileAssociation.ContextVerb}");
                check(verb is null, "卸载移除自身各类右键入口");
            }
            check(FileAssociation.RegisteredExecutable(root) is null && !FileAssociation.IsContextMenuRegistered(installedExe, extensions, root),
                "卸载后应用注册及右键状态恢复未注册");
            check(Read(@"Classes\Directory\shell\OtherApp") == "keep" && Read(@"Fallery\OtherSettings") == "keep"
                && Read(@"Classes\.jpg") == "OtherApp.Image", "卸载保留其他程序入口、默认应用及非关联设置");
            check(ShellRegistrationCommand.TryExecute(["--unregister-shell"], installedExe, extensions, out int again, root) && again == 0,
                "重复卸载幂等成功");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); }
    }
}
