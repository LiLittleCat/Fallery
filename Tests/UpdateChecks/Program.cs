using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Fallery.Services;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
}
async Task Reject(Func<Task> action, string description)
{
    try { await action(); }
    catch { checks++; return; }
    throw new InvalidOperationException(description);
}

Check(AppIdentity.IsDevelopment && AppIdentity.Name == "Fallery.Dev", "本地 Release 构建也必须默认隔离开发数据");
Check(AppIdentity.DataDirectory.EndsWith("Fallery.Dev") && AppIdentity.MotionDirectory.EndsWith("Fallery.Dev-Motion"), "偏好与实况临时文件均隔离");
var disabled = new AppUpdateService(null, "disabled");
await disabled.CheckAsync(); await disabled.DownloadAsync();
Check(disabled.Status.State == AppUpdateState.Disabled, "开发版不能访问更新源");
foreach (string source in new[] { "http://example.com", "https://user:secret@example.com", "https://example.com/?token=secret", "file:///C:/updates", "https://github.com/user/repo/releases" })
    await Reject(() => Task.FromResult(VelopackUpdateBackend.CreateSource(source, false)), "拒绝不安全或不正确的更新源");
Check(VelopackUpdateBackend.CreateSource("https://github.com/LiLittleCat/Fallery", false) is GithubStableUpdateSource,
    "正式仓库使用公开发行清单，不依赖 GitHub API 配额");
Check(VelopackUpdateBackend.CreateSource("https://github.com/LiLittleCat/Fallery", true) is GithubSource,
    "预览版保留 GitHub 预览发行发现，不使用仅含正式版的 latest");
Check(VelopackUpdateBackend.CreateSource("https://example.com/releases", false) is SimpleWebSource, "支持未来切换 HTTPS 静态更新源");

var fake = new FakeBackend();
var service = new AppUpdateService(fake);
await service.ApplyAsync(() => throw new Exception("不应保存"), () => throw new Exception("不应关闭"));
Check(fake.ApplyCount == 0, "未下载不能开始替换");
fake.CheckGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
var checking = service.CheckAsync();
await service.CheckAsync(); await service.DownloadAsync();
Check(fake.CheckCount == 1 && service.Status.State == AppUpdateState.Checking, "多窗口同时检查只发起一个请求");
fake.CheckGate.SetResult(); await checking;
Check(service.Status.State == AppUpdateState.Available, "发现新版本");
fake.CheckGate = null;
fake.DownloadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
var downloading = service.DownloadAsync();
await service.DownloadAsync();
Check(fake.DownloadCount == 1, "多窗口下载共用一个任务");
service.Cancel(); await downloading;
Check(service.Status.State == AppUpdateState.Available && service.Status.Message.Contains("取消"), "取消后可重新下载");
fake.DownloadGate = null;
fake.DownloadError = new IOException("网络断开");
await service.DownloadAsync();
Check(service.Status.State == AppUpdateState.Available && fake.ApplyCount == 0, "失败下载不得进入可安装状态");
fake.DownloadError = null;
await service.DownloadAsync();
Check(service.Status.State == AppUpdateState.Ready && service.Status.Progress == 100, "下载成功后才能重启");
fake.LastProgress?.Invoke(35);
Check(service.Status.State == AppUpdateState.Ready, "迟到的下载进度不能覆盖下载完成状态");
await service.CheckAsync();
Check(service.Status.State == AppUpdateState.Ready, "重复检查保留已经下载的包");
bool closed = false;
await service.ApplyAsync(() => throw new IOException("磁盘不可写"), () => closed = true);
Check(!closed && fake.ApplyCount == 0 && service.Status.State == AppUpdateState.Ready, "保存失败不得启动更新器或关闭窗口");
fake.ApplyError = new IOException("更新器不可用");
await service.ApplyAsync(() => Task.CompletedTask, () => closed = true);
Check(!closed && service.Status.State == AppUpdateState.Ready, "启动更新器失败保留可重试状态");
fake.ApplyError = null;
fake.Events.Clear();
await service.ApplyAsync(() => { fake.Events.Add("save"); return Task.CompletedTask; }, () => { closed = true; fake.Events.Add("close"); });
Check(closed && fake.Events.SequenceEqual(new[] { "save", "schedule", "close" }), "顺序必须是保存、启动等待退出的更新器、关闭窗口");
fake = new FakeBackend { Result = null };
service = new(fake);
await service.CheckAsync();
Check(service.Status.State == AppUpdateState.Latest, "没有更新时显示最新");
fake.CheckError = new HttpRequestException("404", null, System.Net.HttpStatusCode.NotFound);
await service.CheckAsync();
Check(service.Status.State == AppUpdateState.Failed && service.Status.Message.Contains("公开"), "私有仓库 404 不应显示已是最新");
fake.CheckError = null;
fake.CheckGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
checking = service.CheckAsync(); service.Cancel(); await checking;
Check(service.Status.State == AppUpdateState.Idle, "检查可以取消且不留下忙碌状态");
Check(new AppUpdateService(new FakeBackend { Pending = FakeBackend.Release }).Status.State == AppUpdateState.Ready, "重启程序后识别已经下载的更新");

string root = Path.Combine(Path.GetTempPath(), "Fallery-UpdateChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string data = Path.Combine(root, "data"); Directory.CreateDirectory(data);
    await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), "{\"Theme\":2}");
    await File.WriteAllTextAsync(Path.Combine(data, "resume.json"), "[]");
    await File.WriteAllTextAsync(Path.Combine(data, "media.jpg"), "must not copy");
    await UpdateDataBackup.SaveAsync(data);
    string backup = Directory.GetDirectories(Path.Combine(data, "Backups")).Single();
    Check(await File.ReadAllTextAsync(Path.Combine(backup, "settings.json")) == "{\"Theme\":2}" && File.Exists(Path.Combine(backup, "resume.json")), "升级前备份设置和续播");
    Check(!File.Exists(Path.Combine(backup, "media.jpg")) && Directory.GetFiles(backup).Length == 3, "升级备份不复制原媒体");

    string feed = Path.Combine(root, "feed"); Directory.CreateDirectory(feed);
    string packageName = "Fallery.App-0.2.0-full.nupkg";
    string package = Path.Combine(feed, packageName);
    using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
    {
        using var nuspec = new StreamWriter(zip.CreateEntry("Fallery.App.nuspec").Open());
        nuspec.Write("<package><metadata><id>Fallery.App</id><version>0.2.0</version><authors>test</authors><description>test</description></metadata></package>");
    }
    byte[] bytes = await File.ReadAllBytesAsync(package);
    string feedFile = Path.Combine(feed, "releases.stable-win-x64.json");
    await File.WriteAllTextAsync(feedFile, JsonSerializer.Serialize(new { Assets = new[] { new {
        PackageId = "Fallery.App", Version = "0.2.0", Type = "Full", FileName = packageName,
        SHA1 = Convert.ToHexString(SHA1.HashData(bytes)), SHA256 = Convert.ToHexString(SHA256.HashData(bytes)), Size = bytes.Length, NotesMarkdown = "test" } } }));
    string packages = Path.Combine(root, "packages"); Directory.CreateDirectory(packages);
    var locator = new TestVelopackLocator("Fallery.App", "0.1.0", packages, root, root, Path.Combine(root, "Update.exe"), "stable-win-x64");
    var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(feed)), new UpdateOptions { ExplicitChannel = "stable-win-x64", AllowVersionDowngrade = false }, locator);
    var backend = new VelopackUpdateBackend(manager, "Fallery.App");
    var release = await backend.CheckAsync(CancellationToken.None);
    Check(release?.Version == "0.2.0", "真实 SDK 按通道找到新版包");
    await backend.DownloadAsync(release!, _ => { }, CancellationToken.None);
    Check(File.Exists(Path.Combine(packages, packageName)) && backend.Pending?.Version == "0.2.0", "真实 SDK 下载、校验并标记待重启");
    await Reject(async () => await new VelopackUpdateBackend(manager, "Fallery.Preview.App").CheckAsync(CancellationToken.None), "拒绝其他产品的更新包");
    var newer = new UpdateManager(new SimpleFileSource(new DirectoryInfo(feed)), new UpdateOptions { ExplicitChannel = "stable-win-x64", AllowVersionDowngrade = false },
        new TestVelopackLocator("Fallery.App", "0.3.0", packages));
    Check(await new VelopackUpdateBackend(newer, "Fallery.App").CheckAsync(CancellationToken.None) is null, "不向旧版降级");
    var wrongChannel = new UpdateManager(new SimpleFileSource(new DirectoryInfo(feed)), new UpdateOptions { ExplicitChannel = "stable-win-arm64" }, locator);
    Check(await new VelopackUpdateBackend(wrongChannel, "Fallery.App").CheckAsync(CancellationToken.None) is null, "不跨架构读取更新");
    File.Delete(Path.Combine(packages, packageName));
    await File.AppendAllTextAsync(package, "tampered");
    await Reject(() => backend.DownloadAsync(release!, _ => { }, CancellationToken.None), "校验失败不能接受篡改的包");
    Check(!File.Exists(Path.Combine(packages, packageName)), "坏包不能进入可安装目录");

    var downloader = new FeedDownloader(await File.ReadAllTextAsync(feedFile), bytes);
    string directPackages = Path.Combine(root, "direct-packages"); Directory.CreateDirectory(directPackages);
    var directSource = VelopackUpdateBackend.CreateSource("https://github.com/LiLittleCat/Fallery/", false, downloader);
    var directManager = new UpdateManager(directSource,
        new UpdateOptions { ExplicitChannel = "stable-win-x64", AllowVersionDowngrade = false },
        new TestVelopackLocator("Fallery.App", "0.1.0", directPackages));
    var directBackend = new VelopackUpdateBackend(directManager, "Fallery.App");
    var directRelease = await directBackend.CheckAsync(CancellationToken.None);
    Check(directRelease?.Version == "0.2.0", "公开清单可通过真实 SDK 发现更新");
    Check(new Uri(downloader.Requests.Single()).AbsolutePath == "/LiLittleCat/Fallery/releases/latest/download/releases.stable-win-x64.json",
        "正式版仅请求架构对应的 latest 清单");
    // 模拟检查后又发布了新版本，下载仍须使用原先已校验的版本标签。
    downloader.Feed = downloader.Feed.Replace("0.2.0", "0.3.0");
    await directBackend.DownloadAsync(directRelease!, _ => { }, CancellationToken.None);
    Check(downloader.Requests.Last() == "https://github.com/LiLittleCat/Fallery/releases/download/v0.2.0/" + packageName,
        "下载固定到检查时的版本，不受 latest 变化影响");
    Check(directBackend.Pending?.Version == "0.2.0", "直接下载仍通过 SDK 哈希校验并识别待安装版本");
    Check(downloader.Requests.All(url => new Uri(url).Host == "github.com") && !downloader.UsedAuthorization,
        "正式版检查与下载都不访问 API 或携带令牌");
    foreach (string fileName in new[] { "https://other.example/package.nupkg", "../package.nupkg", "..\\package.nupkg", "%2e%2e%2fpackage.nupkg" })
    {
        var invalid = VelopackAssetFeed.FromJson(JsonSerializer.Serialize(new { Assets = new[] { new {
            PackageId = "Fallery.App", Version = "0.2.0", Type = "Full", FileName = fileName } } })).Assets.Single();
        await Reject(() => directSource.DownloadReleaseEntry(null!, invalid, Path.Combine(root, "rejected.nupkg"), _ => { }, CancellationToken.None),
            "清单文件名不能绕过固定版本下载地址");
    }
    downloader.Error = new HttpRequestException("404", null, System.Net.HttpStatusCode.NotFound);
    await Reject(() => directBackend.CheckAsync(CancellationToken.None), "公开清单不可访问时不能返回已是最新");

    if (args.Contains("--online"))
    {
        var online = new UpdateManager(VelopackUpdateBackend.CreateSource("https://github.com/LiLittleCat/Fallery", false),
            new UpdateOptions { ExplicitChannel = "stable-win-x64", AllowVersionDowngrade = false },
            new TestVelopackLocator("Fallery.App", "0.1.0", Path.Combine(root, "online-packages")));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var onlineRelease = await new VelopackUpdateBackend(online, "Fallery.App").CheckAsync(timeout.Token);
        Check(onlineRelease is not null, "真实匿名公开清单可发现比 0.1.0 更新的正式版");
        Console.WriteLine($"ONLINE: found {onlineRelease!.Version}; no update package downloaded or installed.");
    }
}
finally { Directory.Delete(root, recursive: true); }
Console.WriteLine($"PASS: {checks} update checks (isolated files, no installer launched, no user registry changes)");

sealed class FeedDownloader(string feed, byte[] package) : IFileDownloader
{
    public string Feed { get; set; } = feed;
    public Exception? Error { get; set; }
    public bool UsedAuthorization { get; private set; }
    public List<string> Requests { get; } = [];
    private void Record(string url, IDictionary<string, string>? headers)
    {
        Requests.Add(url);
        UsedAuthorization |= headers?.Keys.Any(key => key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) == true;
        if (Error is not null) throw Error;
    }
    public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
    {
        Record(url, headers);
        return Task.FromResult(Feed);
    }
    public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
    {
        Record(url, headers);
        return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(Feed));
    }
    public async Task DownloadFile(string url, string targetFile, Action<int>? progress = null,
        IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
    {
        Record(url, headers);
        await File.WriteAllBytesAsync(targetFile, package, cancelToken);
        progress?.Invoke(100);
    }
}

sealed class FakeBackend : IAppUpdateBackend
{
    public static readonly AppUpdateRelease Release = new("0.2.0", "更新内容", new object());
    public AppUpdateRelease? Pending { get; set; }
    public AppUpdateRelease? Result { get; set; } = Release;
    public TaskCompletionSource? CheckGate { get; set; }
    public TaskCompletionSource? DownloadGate { get; set; }
    public Exception? CheckError { get; set; }
    public Exception? DownloadError { get; set; }
    public Exception? ApplyError { get; set; }
    public int CheckCount, DownloadCount, ApplyCount;
    public Action<int>? LastProgress;
    public List<string> Events { get; } = [];
    public async Task<AppUpdateRelease?> CheckAsync(CancellationToken ct)
    {
        CheckCount++;
        if (CheckGate is { } gate) await gate.Task.WaitAsync(ct);
        if (CheckError is not null) throw CheckError;
        return Result;
    }
    public async Task DownloadAsync(AppUpdateRelease release, Action<int> progress, CancellationToken ct)
    {
        DownloadCount++;
        LastProgress = progress;
        progress(25);
        if (DownloadGate is { } gate) await gate.Task.WaitAsync(ct);
        if (DownloadError is not null) throw DownloadError;
        progress(100);
    }
    public void ScheduleRestart(AppUpdateRelease release)
    {
        if (ApplyError is not null) throw ApplyError;
        ApplyCount++; Events.Add("schedule");
    }
}
