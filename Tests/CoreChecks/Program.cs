using System.Diagnostics;
using Fallery.Layouts;
using Fallery.Models;
using Fallery.Services;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using FileAttributes = System.IO.FileAttributes;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("自然排序测试需要 Windows。");
var workspace = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
if (args.Length == 3 && args[1] == "--keyframe-sample")
{
    await VideoKeyframeChecks.RunAsync(Path.GetFullPath(args[2]));
    return;
}
if (args.Length == 3 && args[1] == "--seek-sample")
{
    await VideoSeekDiagnostics.RunAsync(Path.GetFullPath(args[2]));
    return;
}
var fixture = Path.Combine(workspace, "work", "validation", "corechecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
if (args.Length == 4 && args[1] == "--identical-sample")
{
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var result = await IdenticalImages.ScanAsync(Path.GetFullPath(args[2]), [Path.GetFullPath(args[3])], false, null, timeout.Token);
        Console.WriteLine($"Read-only identical image scan: {result.Examined} images, {result.Matches.Count} matches, {result.Skipped} skipped, limited={result.Limited}. No files deleted.");
    }
    finally { Directory.Delete(fixture, recursive: true); }
    return;
}
if (args.Length == 3 && args[1] == "--search-sample")
{
    try { await MediaSearchChecks.CheckLocalSampleAsync(fixture, Path.GetFullPath(args[2])); }
    finally { Directory.Delete(fixture, recursive: true); }
    return;
}
int checks = 0;
checks += await FolderExclusionChecks.RunAsync(fixture);
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
}
void Expect<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { checks++; return; }
    throw new InvalidOperationException(name);
}

var detailsGeometry = MasonryGeometry.Details(10000, 400);
Check(detailsGeometry.Bounds[0].Width == MasonryGeometry.DetailsMinWidth && detailsGeometry.ExtentHeight == 600000,
    "列表视图窄窗口保留所有列，万项采用固定行高");
Check(detailsGeometry.GetVisibleIndices(60000, 600).SequenceEqual(Enumerable.Range(1000, 10)), "列表视图只查询可见行");
Check(MasonryGeometry.Details(0, 1200).ExtentHeight == 0, "空列表无额外滚动高度");
Check((int)GalleryDisplayMode.Justified == 3 && (int)GalleryDisplayMode.Details == 4
    && new AppSettings { DisplayMode = GalleryDisplayMode.Details }.Normalize().DisplayMode == GalleryDisplayMode.Details, "列表模式追加枚举且可持久化");
var detailFile = new MediaFile(@"C:\sample\photo.jpg", MediaKind.Image)
{
    Version = new(@"C:\SAMPLE\PHOTO.JPG", 1536, new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc).Ticks),
    CreatedTicks = new DateTime(2025, 2, 3, 4, 5, 0, DateTimeKind.Utc).Ticks
};
Check(detailFile.SizeText == "1.5 KB" && detailFile.TypeText == "JPG 图片", "列表显示真实格式和文件大小");
Check(detailFile.CreatedText != detailFile.ModifiedText && detailFile.CreatedText.StartsWith("2025-02-03"), "创建时间和修改时间分别显示");
Check(new MediaFile(@"C:\sample", MediaKind.Folder).SizeText == "—"
    && new MediaFile(@"C:\sample", MediaKind.Folder).CreatedText == "—", "文件夹大小和未知日期不伪造数值");

foreach (var name in new[] { "image10.JPG", "image2.png", "image1.jpg", "video2.MP4", "video10.mov", "ignore.txt", "hidden.jpg", "system.jpg" })
    File.WriteAllText(Path.Combine(fixture, name), "fixture");
File.SetAttributes(Path.Combine(fixture, "hidden.jpg"), FileAttributes.Hidden);
File.SetAttributes(Path.Combine(fixture, "system.jpg"), FileAttributes.System);
foreach (var name in new[] { "folder10", "folder2", "hidden-folder", "system-folder", "empty" }) Directory.CreateDirectory(Path.Combine(fixture, name));
File.SetAttributes(Path.Combine(fixture, "hidden-folder"), FileAttributes.Hidden);
File.SetAttributes(Path.Combine(fixture, "system-folder"), FileAttributes.System);
File.WriteAllText(Path.Combine(fixture, "folder2", "nested.jpg"), "fixture");
Directory.CreateDirectory(Path.Combine(fixture, "folder2", "child"));
Check(FolderAddress.Resolve("  \"E:\\照片 空格\\相册\"  ", null) == @"E:\照片 空格\相册", "地址栏应支持资源管理器复制的带引号路径");
Check(FolderAddress.Resolve("E:", null) == @"E:\", "盘符应跳转到根目录");
Check(FolderAddress.Resolve(@"..\folder2", Path.Combine(fixture, "folder10")) == Path.Combine(fixture, "folder2"), "相对地址必须基于当前浏览目录");
Check(FolderAddress.Resolve(@"\\server\share\照片\..\视频", null) == @"\\server\share\视频", "网络共享路径解析错误");
Check(FolderAddress.Resolve("%TEMP%", null) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.GetEnvironmentVariable("TEMP")!)), "环境变量路径解析错误");
foreach (var invalid in new[] { "", " ", "https://example.com", @"E:\bad*name", "E:\\bad\0name", @"E:relative", @"E:\bad:stream" })
    Expect<ArgumentException>(() => FolderAddress.Resolve(invalid, fixture), $"非法地址应被拒绝：{invalid.Replace('\0', '?')}");
Check(FolderAddress.GetChildren(fixture).Select(c => c.Name).SequenceEqual(new[] { "empty", "folder2", "folder10" }),
    "地址菜单只列当前层子目录，按自然顺序排列并过滤隐藏和系统目录");
Expect<DirectoryNotFoundException>(() => FolderAddress.GetChildren(Path.Combine(fixture, "missing")), "地址菜单读取失败不能伪装为空目录");
using (var canceledAddress = new CancellationTokenSource())
{
    canceledAddress.Cancel();
    Expect<OperationCanceledException>(() => FolderAddress.GetChildren(fixture, canceledAddress.Token), "关闭子目录菜单必须支持取消读取");
}
var media = FolderScanner.Scan(fixture);
Check(media.All(m => m.Version is not null), "扫描必须携带文件版本，返回目录才能校验缓存");
var originalVersion = media[0].Version;
Check(originalVersion == FileVersion.Read(media[0].Path.ToUpperInvariant()), "Windows 路径大小写应命中同一缓存");
File.AppendAllText(media[0].Path, "changed");
Check(originalVersion != FileVersion.Read(media[0].Path), "覆盖文件后缓存版本必须失效");
media = FolderScanner.Scan(fixture);
var lru = new BoundedCache<string, string>(10, 3);
lru.Set("a", "A", 4);
lru.Set("b", "B", 4);
Check(lru.TryGet("a", out var hit) && hit == "A", "缓存命中应返回原值并更新访问顺序");
lru.Set("c", "C", 4);
Check(!lru.TryGet("b", out _) && lru.TryGet("a", out _), "预算不足应淘汰最久未用项");
lru.Set("a", "large", 8);
Check(lru.Count == 1 && lru.Weight == 8 && !lru.TryGet("c", out _), "同键替换必须更新预算并继续淘汰");
lru.Set("huge", "H", 11);
Check(lru.Count == 1 && lru.Weight <= 10, "超出预算的单项不能进入缓存");
lru.Clear();
for (int i = 0; i < 10; i++) lru.Set(i.ToString(), "small");
Check(lru.Count == 3 && lru.Weight == 3, "小对象也必须受条目数量限制");
Parallel.For(0, 1000, i => { lru.Set(i.ToString(), "parallel", 2); lru.TryGet((i - 1).ToString(), out _); });
Check(lru.Count <= 3 && lru.Weight <= 10, "后台并发不能突破缓存限制");
lru.Clear();
Check(lru.Count == 0 && lru.Weight == 0, "清理必须释放所有缓存引用和计数");
Check(media.Select(m => m.Name).SequenceEqual(new[] { "image1.jpg", "image2.png", "image10.JPG", "video2.MP4", "video10.mov" }),
    "扩展名过滤、自然排序、隐藏/系统文件过滤或不递归规则错误");
Check(media.Count(m => m.Kind == MediaKind.Video) == 2, "视频分类错误");
Check(FolderScanner.Scan(Path.Combine(fixture, "empty")).Count == 0, "空目录扫描错误");
Check(FolderScanner.GetSubfolders(fixture).Select(Path.GetFileName).SequenceEqual(new[] { "empty", "folder2", "folder10" }), "子目录排序或过滤错误");
Check(!FolderScanner.HasSubfolders(Path.Combine(fixture, "empty")), "空目录子节点状态错误");
Check(FolderScanner.GetSubfolders(Path.Combine(fixture, "missing")).Count == 0, "不存在的树节点应安全跳过");
var children = FolderScanner.GetChildFolders(fixture);
Check(children.Select(c => Path.GetFileName(c.Path)).SequenceEqual(new[] { "empty", "folder2", "folder10" }), "可访问子目录过滤及自然排序错误");
Check(children.Select(c => c.HasChildren).SequenceEqual(new[] { false, true, false }), "懒加载箭头状态错误");
Check(FolderScanner.GetChildFolders(Path.Combine(fixture, "missing")).Count == 0, "失效目录的展开应安全返回");
Expect<DirectoryNotFoundException>(() => FolderScanner.Scan(Path.Combine(fixture, "missing")), "扫描失败不能伪装成空目录");
var entries = FolderScanner.ScanEntries(fixture);
Check(entries.Where(e => e.Kind == MediaKind.Folder).Select(e => e.Name).SequenceEqual(new[]
{
    "folder2",
}), "文件夹卡片必须过滤无媒体目录，并保留含媒体的子目录");
Check(entries.Where(e => e.Kind != MediaKind.Folder).SequenceEqual(media), "混排不能改变媒体播放列表或递归展开子目录文件");
var previews = FolderScanner.FindFolderPreviews(fixture);
Check(previews.Count == 6 && previews.Take(5).All(media.Contains) && previews[5].Name == "nested.jpg",
    "封面候选先当前层再子目录，跳过隐藏、系统及非媒体文件");
Check(FolderScanner.FindFolderPreviews(Path.Combine(fixture, "folder2")).Single().Name == "nested.jpg", "图片文件夹封面候选错误");
Check(FolderScanner.FindFolderPreviews(Path.Combine(fixture, "empty")).Count == 0, "空目录不应有封面候选");
Check(FolderScanner.FindFolderPreviews(Path.Combine(fixture, "missing")).Count == 0, "失效目录封面应安全回退");
File.WriteAllText(Path.Combine(fixture, "folder10", "video.mp4"), "fixture");
Check(FolderScanner.FindFolderPreviews(Path.Combine(fixture, "folder10")).Single().Kind == MediaKind.Video, "纯视频目录也应有封面候选");
var photoFiltered = FolderScanner.ScanEntries(fixture, filter: MediaFilter.Photos);
Check(photoFiltered.All(f => f.Kind != MediaKind.Video) && photoFiltered.Any(f => f.Name == "folder2")
    && photoFiltered.All(f => f.Name != "folder10"), "图片筛选应过滤视频文件及仅有视频的子文件夹");
var videoFiltered = FolderScanner.ScanEntries(fixture, filter: MediaFilter.Videos);
Check(videoFiltered.All(f => f.Kind != MediaKind.Image) && videoFiltered.Any(f => f.Name == "folder10")
    && videoFiltered.All(f => f.Name != "folder2"), "视频筛选应过滤图片文件及仅有图片的子文件夹");
Check(FolderScanner.ScanEntries(fixture, showSubfolders: false).All(f => f.Kind != MediaKind.Folder), "隐藏子文件夹应只显示当前层媒体");
Check(FolderScanner.FindFolderPreviews(fixture, filter: MediaFilter.Videos).All(f => f.Kind == MediaKind.Video), "叠放封面应遵守媒体筛选");
Check(media.All(f => f.CreatedTicks is > 0), "创建时间必须从文件系统读取");
var parentOnly = Path.Combine(fixture, "parent-only");
Directory.CreateDirectory(Path.Combine(parentOnly, "album"));
File.WriteAllText(Path.Combine(parentOnly, "album", "photo.jpg"), "fixture");
Check(FolderScanner.ScanEntries(parentOnly).Single().Kind == MediaKind.Folder, "只有子文件夹的上级目录不能显示为空");
Check(FolderScanner.FindFolderPreviews(parentOnly).Single().Name == "photo.jpg", "只有文件夹的目录应从后代媒体生成封面");
Check(FolderScanner.ContainsMediaRecursive(parentOnly), "存在后代媒体时必须保留上级入口");
File.Delete(Path.Combine(parentOnly, "album", "photo.jpg"));
Check(!FolderScanner.ContainsMediaRecursive(parentOnly) && FolderScanner.ScanEntries(parentOnly).Count == 0,
    "删除最后一个后代媒体后应立即隐藏空分支，不能保留旧判断");
File.WriteAllText(Path.Combine(parentOnly, "album", "new.mp4"), "fixture");
Check(FolderScanner.ScanEntries(parentOnly).Count == 1 && FolderScanner.FindFolderPreviews(parentOnly).Single().Kind == MediaKind.Video,
    "新增后代视频后应重新显示目录并提供视频封面");
var deepFolder = Directory.CreateDirectory(Path.Combine(parentOnly, "album", "deep", "deeper")).FullName;
File.WriteAllText(Path.Combine(deepFolder, "deep.jpg"), "fixture");
Check(FolderScanner.FindFolderPreviews(parentOnly).Count == 2, "封面必须跨多层查找，不能只检查一级子目录");
var textOnly = Directory.CreateDirectory(Path.Combine(parentOnly, "text-only", "nested")).FullName;
File.WriteAllText(Path.Combine(textOnly, "notes.txt"), "fixture");
Check(FolderScanner.ScanEntries(parentOnly).All(f => f.Name != "text-only"), "只有非媒体文件的后代分支应隐藏");
Check(await MediaProbe.TryGetAspectRatioAsync(parentOnly, MediaKind.Folder) is null, "文件夹不能当作媒体探测");
Expect<DirectoryNotFoundException>(() => FolderScanner.ScanEntries(Path.Combine(fixture, "missing")), "目录卡片扫描也必须保留失败语义");
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    Expect<OperationCanceledException>(() => FolderScanner.Scan(Path.Combine(fixture, "empty"), canceled.Token), "空目录也应响应取消");
    Expect<OperationCanceledException>(() => FolderScanner.GetSubfolders(fixture, canceled.Token), "目录树应响应取消");
    Expect<OperationCanceledException>(() => FolderScanner.GetChildFolders(fixture, canceled.Token), "子目录探测应响应取消");
    Expect<OperationCanceledException>(() => FolderScanner.GetRoots(canceled.Token), "磁盘枚举应响应取消");
    Expect<OperationCanceledException>(() => FolderScanner.ScanEntries(fixture, canceled.Token), "卡片扫描应响应取消");
    Expect<OperationCanceledException>(() => FolderScanner.FindFolderPreviews(fixture, canceled.Token), "封面候选查询应响应取消");
    Expect<OperationCanceledException>(() => FolderScanner.ContainsMediaRecursive(parentOnly, canceled.Token), "后代媒体判断应响应取消");
}

var navRoot = Path.Combine(fixture, "navigation");
var navPaths = new[] { "", "album1", @"album1\sub2", @"album1\sub10", @"album2\deep", "album10" }
    .Select(p => Path.Combine(navRoot, p)).ToArray();
foreach (var (path, index) in navPaths.Select((p, i) => (p, i)))
{
    Directory.CreateDirectory(path);
    File.WriteAllText(Path.Combine(path, index % 2 == 0 ? "photo.jpg" : "movie.mp4"), "fixture");
}
Directory.CreateDirectory(Path.Combine(navRoot, "album3-empty"));
var hiddenAlbum = Directory.CreateDirectory(Path.Combine(navRoot, "album4-hidden"));
File.WriteAllText(Path.Combine(hiddenAlbum.FullName, "photo.jpg"), "fixture");
hiddenAlbum.Attributes |= FileAttributes.Hidden;
try { Directory.CreateSymbolicLink(Path.Combine(navRoot, "album5-loop"), navRoot); }
catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { Console.WriteLine("Symlink fixture unavailable; traversal still skips reparse points by enumeration policy."); }
for (int i = 0; i < navPaths.Length; i++)
{
    Check(FolderNavigator.FindAdjacent(navPaths[i], true, boundary: navRoot) == (i + 1 < navPaths.Length ? navPaths[i + 1] : null), "向前深度优先遍历顺序/自然排序/跳过空目录错误");
    Check(FolderNavigator.FindAdjacent(navPaths[i], false, boundary: navRoot) == (i > 0 ? navPaths[i - 1] : null), "向后遍历必须与向前互逆，包含祖先及上一个分支");
}
using (var canceledNav = new CancellationTokenSource())
{
    canceledNav.Cancel();
    Expect<OperationCanceledException>(() => FolderNavigator.FindAdjacent(navPaths[0], true, canceledNav.Token, navRoot), "目录跳转必须响应取消");
    Expect<OperationCanceledException>(() => FolderScanner.HasMedia(navRoot, canceledNav.Token), "只探测首个媒体也要响应取消");
}
Expect<ArgumentException>(() => FolderNavigator.FindAdjacent(fixture, true, boundary: navRoot), "遍历不能越过指定根目录");
Check(FolderNavigator.FindAdjacent(Path.Combine(navRoot, "album3-empty"), true, boundary: navRoot) == navPaths[^1], "空目录也应能跳到下一个媒体目录");

var hiddenRoot = Directory.CreateDirectory(Path.Combine(fixture, "hidden-items")).FullName;
File.WriteAllText(Path.Combine(hiddenRoot, "visible.jpg"), "fixture");
File.WriteAllText(Path.Combine(hiddenRoot, "secret.jpg"), "fixture");
File.SetAttributes(Path.Combine(hiddenRoot, "secret.jpg"), FileAttributes.Hidden);
File.WriteAllText(Path.Combine(hiddenRoot, "protected.jpg"), "fixture");
File.SetAttributes(Path.Combine(hiddenRoot, "protected.jpg"), FileAttributes.Hidden | FileAttributes.System);
var hiddenMedia = Directory.CreateDirectory(Path.Combine(hiddenRoot, ".album"));
File.WriteAllText(Path.Combine(hiddenMedia.FullName, "photo.jpg"), "fixture");
hiddenMedia.Attributes |= FileAttributes.Hidden;
var hiddenDeep = Directory.CreateDirectory(Path.Combine(hiddenRoot, ".deep", "inner"));
File.WriteAllText(Path.Combine(hiddenDeep.FullName, "clip.mp4"), "fixture");
File.SetAttributes(Path.Combine(hiddenRoot, ".deep"), FileAttributes.Hidden);
var hiddenEmpty = Directory.CreateDirectory(Path.Combine(hiddenRoot, ".cache", "nested"));
File.WriteAllText(Path.Combine(hiddenEmpty.FullName, "notes.txt"), "fixture");
File.SetAttributes(Path.Combine(hiddenRoot, ".cache"), FileAttributes.Hidden);
var visibleWithHiddenMedia = Directory.CreateDirectory(Path.Combine(hiddenRoot, "plain", ".inside"));
File.WriteAllText(Path.Combine(visibleWithHiddenMedia.FullName, "photo.jpg"), "fixture");
visibleWithHiddenMedia.Attributes |= FileAttributes.Hidden;
Directory.CreateDirectory(Path.Combine(hiddenRoot, "plain-empty"));
var protectedFolder = Directory.CreateDirectory(Path.Combine(hiddenRoot, "protected-folder"));
File.WriteAllText(Path.Combine(protectedFolder.FullName, "photo.jpg"), "fixture");
protectedFolder.Attributes |= FileAttributes.Hidden | FileAttributes.System;
Check(FolderScanner.Scan(hiddenRoot).Select(m => m.Name).SequenceEqual(new[] { "visible.jpg" }), "默认不显示隐藏媒体文件");
Check(FolderScanner.Scan(hiddenRoot, showHidden: true).Select(m => m.Name).SequenceEqual(new[] { "secret.jpg", "visible.jpg" }),
    "显示隐藏项目时包含隐藏媒体，但仍跳过系统受保护文件");
Check(FolderScanner.ScanEntries(hiddenRoot).Where(e => e.Kind == MediaKind.Folder).Select(e => e.Name).SequenceEqual(Array.Empty<string>()),
    "默认不显示隐藏目录，也不把只含隐藏媒体的普通目录当作媒体目录");
var hiddenEntries = FolderScanner.ScanEntries(hiddenRoot, showHidden: true);
Check(hiddenEntries.Where(e => e.Kind == MediaKind.Folder).Select(e => e.Name).SequenceEqual(new[] { ".album", ".deep", "plain" }),
    "显示隐藏项目时只列出含媒体的目录，空隐藏目录与系统目录不显示");
Check(hiddenEntries.Where(e => e.Kind == MediaKind.Folder).All(e => e.PreviewHidden), "文件夹封面查找必须沿用隐藏项目选项");
Check(FolderScanner.FindFolderPreviews(Path.Combine(hiddenRoot, "plain"), showHidden: true).Count == 1
    && FolderScanner.FindFolderPreviews(Path.Combine(hiddenRoot, "plain")).Count == 0, "封面查找按选项进入隐藏子目录");
Check(FolderScanner.GetChildFolders(hiddenRoot).Select(c => Path.GetFileName(c.Path)).SequenceEqual(new[] { "plain", "plain-empty" }),
    "文件树默认只列普通目录");
var hiddenTree = FolderScanner.GetChildFolders(hiddenRoot, showHidden: true);
Check(hiddenTree.Select(c => Path.GetFileName(c.Path)).SequenceEqual(new[] { ".album", ".deep", "plain", "plain-empty" }),
    "文件树显示隐藏项目时只加入含媒体的隐藏目录，普通目录保持完整导航");
Check(hiddenTree.Select(c => c.HasChildren).SequenceEqual(new[] { false, true, true, false }), "隐藏子目录的展开箭头状态错误");
Check(!FolderScanner.HasSubfolders(Path.Combine(hiddenRoot, "plain")) && FolderScanner.HasSubfolders(Path.Combine(hiddenRoot, "plain"), showHidden: true),
    "只有含媒体的隐藏子目录才让节点可展开");
Check(FolderAddress.GetChildren(hiddenRoot, showHidden: true).Select(c => c.Name).SequenceEqual(new[] { ".album", ".deep", "plain", "plain-empty" }),
    "地址菜单显示隐藏项目时同样过滤无媒体的隐藏目录");
Check(FolderNavigator.FindAdjacent(Path.Combine(hiddenRoot, "plain-empty"), false, boundary: hiddenRoot) == hiddenRoot,
    "默认 A/D 遍历跳过隐藏目录");
Check(FolderNavigator.FindAdjacent(hiddenRoot, true, boundary: hiddenRoot, showHidden: true) == hiddenMedia.FullName
    && FolderNavigator.FindAdjacent(hiddenMedia.FullName, true, boundary: hiddenRoot, showHidden: true) == hiddenDeep.FullName
    && FolderNavigator.FindAdjacent(hiddenDeep.FullName, true, boundary: hiddenRoot, showHidden: true) == visibleWithHiddenMedia.FullName,
    "显示隐藏项目时 A/D 进入含媒体的隐藏目录，跳过无媒体隐藏目录");

Check(!FolderScanner.ContainsMediaWithin(hiddenEmpty.FullName, MediaFilter.All, true, default), "限时检查在时限内应如实返回无媒体");
Check(FolderScanner.ContainsMediaWithin(hiddenEmpty.FullName, MediaFilter.All, true, default, TimeSpan.Zero),
    "超过时限的目录按可能含媒体保留，不能拖住整页");
using (var canceledProbe = new CancellationTokenSource())
{
    canceledProbe.Cancel();
    Expect<OperationCanceledException>(() => FolderScanner.ContainsMediaWithin(hiddenRoot, MediaFilter.All, true, canceledProbe.Token),
        "外部取消不能被当作超时吞掉");
    Expect<OperationCanceledException>(() => FolderScanner.ScanEntries(hiddenRoot, canceledProbe.Token, showHidden: true), "并发检查子目录必须响应取消");
    Expect<OperationCanceledException>(() => FolderScanner.GetChildFolders(hiddenRoot, canceledProbe.Token, showHidden: true), "并发加载树节点必须响应取消");
}

var devRoot = Directory.CreateDirectory(Path.Combine(fixture, "dev-dirs")).FullName;
foreach (var dir in new[] { @"project\node_modules\pkg", @"project\src", @".npm\_cacache", "node_modules", "album" })
    Directory.CreateDirectory(Path.Combine(devRoot, dir));
File.WriteAllText(Path.Combine(devRoot, @"project\node_modules\pkg\logo.png"), "fixture");
File.WriteAllText(Path.Combine(devRoot, @".npm\_cacache\icon.png"), "fixture");
File.SetAttributes(Path.Combine(devRoot, ".npm"), FileAttributes.Hidden);
File.WriteAllText(Path.Combine(devRoot, @"node_modules\icon.png"), "fixture");
File.WriteAllText(Path.Combine(devRoot, @"album\photo.jpg"), "fixture");
Check(FolderScanner.IsExcludedDirectory(@"C:\x\Node_Modules\") && !FolderScanner.IsExcludedDirectory(@"C:\x\build"), "排除名单按目录名且不区分大小写");
Check(FolderScanner.ScanEntries(devRoot, showHidden: true).Where(e => e.Kind == MediaKind.Folder).Select(e => e.Name).SequenceEqual(new[] { "album" }),
    "依赖和缓存目录不扫描，仅含其中图标的项目目录也不显示");
Check(FolderScanner.GetChildFolders(devRoot, showHidden: true).Select(c => Path.GetFileName(c.Path)).SequenceEqual(new[] { "album", "node_modules", "project" }),
    "文件树保留普通目录导航，但隐藏的开发缓存目录不显示");
Check(FolderNavigator.FindAdjacent(Path.Combine(devRoot, "album"), true, boundary: devRoot, showHidden: true) is null, "A/D 遍历不进入依赖目录");
Check(FolderScanner.Scan(Path.Combine(devRoot, "node_modules")).Count == 1, "明确打开的排除目录仍可查看当前层媒体");

var launchFile = Path.Combine(fixture, "image1.jpg");
Check(StartupArguments.Split("\"C:\\Program Files\\Fallery\\Fallery.exe\" \"E:\\照片 空格\\a.mp4\" --flag")
    .SequenceEqual(new[] { @"C:\Program Files\Fallery\Fallery.exe", @"E:\照片 空格\a.mp4", "--flag" }), "命令行拆分要处理引号、空格和中文");
Check(StartupArguments.FindPath(new[] { @"C:\Fallery.exe", "--verbose", launchFile }) == launchFile, "启动参数应跳过 exe 和开关，取第一个存在的路径");
Check(StartupArguments.FindPath($"\"C:\\x\\Fallery.exe\" \"{launchFile}\"") == launchFile, "重定向的整行命令也要解析出文件");
Check(StartupArguments.FindPath(new[] { Path.Combine(fixture, "missing.jpg") }) is null && StartupArguments.FindPath((string?)null) is null,
    "不存在的路径和空命令行不能当作启动文件");
using (var registryRoot = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\FalleryCoreChecks\{Guid.NewGuid():N}"))
{
    try
    {
        FileAssociation.Register(@"C:\Apps\Fallery\Fallery.exe", new[] { ".mp4", "JPG" }, registryRoot);
        Check(FileAssociation.RegisteredExecutable(registryRoot) == @"C:\Apps\Fallery\Fallery.exe", "注册后应能读回 exe 路径");
        using (var command = registryRoot.OpenSubKey($@"Classes\{FileAssociation.ProgId}\shell\open\command"))
            Check(command?.GetValue("") as string == "\"C:\\Apps\\Fallery\\Fallery.exe\" \"%1\"", "打开命令必须带引号传入文件路径");
        using (var progIds = registryRoot.OpenSubKey(@"Classes\.jpg\OpenWithProgids"))
            Check(progIds?.GetValueNames().Contains(FileAssociation.ProgId) == true, "扩展名统一小写并挂到打开方式列表");
        using (var caps = registryRoot.OpenSubKey($@"{AppIdentity.Name}\Capabilities\FileAssociations"))
            Check(caps?.GetValue(".mp4") as string == FileAssociation.ProgId, "默认应用能力表应列出支持的扩展名");
        FileAssociation.Unregister(new[] { ".mp4", ".jpg" }, registryRoot);
        using (var progIds = registryRoot.OpenSubKey(@"Classes\.jpg\OpenWithProgids"))
            Check(FileAssociation.RegisteredExecutable(registryRoot) is null && progIds?.GetValueNames().Contains(FileAssociation.ProgId) != true,
                "取消注册要清掉应用、ProgID 和扩展名挂载");
    }
    finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\FalleryCoreChecks", throwOnMissingSubKey: false); }
}

ShellRegistrationChecks.Run(fixture, Check);
await AudioChecks.RunAsync(Path.Combine(fixture, "audio-checks"), Check);
await FileClipboardChecks.RunAsync(Path.Combine(fixture, "clipboard-checks"), Check);

var resumePath = Path.Combine(fixture, "resume", "resume.json");
var resumeVersion = new FileVersion(@"C:\VIDEOS\A.MP4", 1000, 42);
var resume = new Fallery.Services.Playback.ResumeStore(resumePath);
Check(!await new Fallery.Services.Playback.ResumeStore(fixture).SaveAsync(resumeVersion, TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(10)),
    "续播目标是目录时明确返回失败，更新不能把失败保存当作完成");
await resume.SaveAsync(resumeVersion, TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(10));
Check(await new Fallery.Services.Playback.ResumeStore(resumePath).GetAsync(resumeVersion) == TimeSpan.FromSeconds(90), "播放进度应写盘并能被新实例读回");
Check(await resume.GetAsync(resumeVersion with { ModifiedTicks = 43 }) is null, "文件被替换后不能沿用旧进度");
await resume.SaveAsync(resumeVersion, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(10));
Check(await resume.GetAsync(resumeVersion) is null, "开头附近不记录进度");
await resume.SaveAsync(resumeVersion, TimeSpan.FromSeconds(597), TimeSpan.FromMinutes(10));
Check(await resume.GetAsync(resumeVersion) is null, "看到结尾附近视为看完，下次从头播放");
for (int i = 0; i < Fallery.Services.Playback.ResumeStore.Limit + 20; i++)
    await resume.SaveAsync(new FileVersion($@"C:\V\{i}.MP4", 1, 1), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
Check(await resume.GetAsync(new FileVersion(@"C:\V\0.MP4", 1, 1)) is null
    && await resume.GetAsync(new FileVersion($@"C:\V\{Fallery.Services.Playback.ResumeStore.Limit + 19}.MP4", 1, 1)) is not null,
    "进度记录有上限，淘汰最久未写入的");

Check(MediaDetailsReader.FormatSize(512) == "512 字节" && MediaDetailsReader.FormatSize(451_482) == "440.9 KB"
    && MediaDetailsReader.FormatSize(8_178_892) == "7.8 MB" && MediaDetailsReader.FormatSize(3L << 30) == "3 GB", "文件大小应按 Windows 习惯换算单位");
Check(MediaDetailsReader.FormatOf(@"C:\a\b.jpeg") == "JPEG" && MediaDetailsReader.FormatOf("clip.Mp4") == "MP4", "格式取扩展名并大写");
var basicDetails = MediaDetailsReader.Basic(launchFile, MediaKind.Image);
Check(basicDetails.Format == "JPG" && basicDetails.Length == new FileInfo(launchFile).Length && basicDetails.Resolution == ""
    && basicDetails.Groups.Any(g => g.Rows.Any(r => r.Value == fixture)), "基础信息不读文件内容，包含格式、大小和所在文件夹");
Check(MediaDetailsReader.Basic(Path.Combine(fixture, "missing.mp4"), MediaKind.Video).Length == 0, "缺失文件的基础信息不能抛异常");
await PhotoMetadataChecks.RunAsync(Path.Combine(fixture, "exif"), Check);
await FilePasteChecks.RunAsync(Path.Combine(fixture, "paste"), Check);
await IdenticalImageChecks.RunAsync(Path.Combine(fixture, "identical"), Check);
await MotionPhotoChecks.RunAsync(Path.Combine(fixture, "motion"), Check);
await MediaSearchChecks.RunAsync(Path.Combine(fixture, "search"), Check);

MediaFile SortFile(string name, long length, long ticks) => new(Path.Combine(fixture, name), MediaKind.Image)
{ Version = new FileVersion(Path.Combine(fixture, name), length, ticks), CreatedTicks = ticks, TakenTicks = ticks };
var sortable = new[]
{
    SortFile("image10.jpg", 10, 2), SortFile("image2.jpg", 30, 1), SortFile("image1.jpg", 20, 3),
    new MediaFile(Path.Combine(fixture, "folder10"), MediaKind.Folder) { FolderModifiedTicks = 1 },
    new MediaFile(Path.Combine(fixture, "folder2"), MediaKind.Folder) { FolderModifiedTicks = 2 },
};
foreach (var descending in new[] { false, true })
{
    foreach (var field in Enum.GetValues<MediaSortField>())
    {
        var sorted = MediaSorter.Sort(sortable, f => f, field, descending);
        Check(sorted.Take(2).All(f => f.Kind == MediaKind.Folder), "所有排序方式都必须保持文件夹优先");
        var expected = field switch
        {
            MediaSortField.Name => new[] { "image1.jpg", "image2.jpg", "image10.jpg" },
            MediaSortField.Size => new[] { "image10.jpg", "image1.jpg", "image2.jpg" },
            _ => new[] { "image2.jpg", "image10.jpg", "image1.jpg" },
        };
        Check(sorted.Skip(2).Select(f => f.Name).SequenceEqual(descending ? expected.Reverse() : expected), "名称/大小/修改时间升降序错误");
        if (field == MediaSortField.Size) Check(sorted[0].Name == "folder2", "大小排序不能递归计算文件夹，也不改变其自然顺序");
    }
    var missingMetadata = new MediaFile(Path.Combine(fixture, "missing.jpg"), MediaKind.Image);
    foreach (var field in new[] { MediaSortField.Size, MediaSortField.Taken, MediaSortField.Created })
        Check(MediaSorter.Sort(sortable.Append(missingMetadata), f => f, field, descending)[^1] == missingMetadata,
            "缺少元数据的文件始终排在这一组末尾");
}
Check(NavigationGesture.Classify(100, 5) == NavigationAction.Next && NavigationGesture.Classify(-100, 5) == NavigationAction.Previous, "左右手势映射错误");
Check(NavigationGesture.Classify(5, -100) == NavigationAction.Parent && NavigationGesture.Classify(5, 100) == NavigationAction.Back, "上下手势映射错误");
Check(NavigationGesture.Classify(20, 0) == NavigationAction.None && NavigationGesture.Classify(90, 90) == NavigationAction.None
    && NavigationGesture.Classify(double.NaN, 100) == NavigationAction.None, "抖动、斜向和无效坐标不应执行手势");

var settingsPath = Path.Combine(fixture, "settings", "settings.json");
var settingsStore = new AppSettingsStore(settingsPath);
bool SameSettings(AppSettings actual, AppSettings expected) =>
    System.Text.Json.JsonSerializer.Serialize(actual.Normalize()) == System.Text.Json.JsonSerializer.Serialize(expected.Normalize());
Check(SameSettings(await settingsStore.LoadAsync(), new AppSettings()), "首次启动应使用默认设置");
var savedSettings = new AppSettings { Width = 1100, Height = 700, Maximized = true, LastFolder = navRoot, Theme = 2,
    BackgroundOpacity = 82, SidebarWidth = 300, ThumbnailSize = 320, SortField = MediaSortField.Modified, Descending = true };
Check(await settingsStore.SaveAsync(savedSettings) && SameSettings(await settingsStore.LoadAsync(), savedSettings), "所有设置必须完整往返保存");
File.WriteAllText(settingsPath, "{broken json");
Check(SameSettings(await settingsStore.LoadAsync(), new AppSettings()), "损坏设置不能阻止启动");
File.WriteAllText(settingsPath, "{\"Theme\":2}");
Check((await settingsStore.LoadAsync()).BackgroundOpacity == 70, "旧设置缺少背景不透明度时应使用更实的默认背景");
Check(new AppSettings { BackgroundOpacity = double.NaN }.Normalize().BackgroundOpacity == 70
    && new AppSettings { BackgroundOpacity = -20 }.Normalize().BackgroundOpacity == 0
    && new AppSettings { BackgroundOpacity = 120 }.Normalize().BackgroundOpacity == 100,
    "背景不透明度应校正非法值并限制范围");
var sanitized = new AppSettings { Width = -1, Height = 99999, Theme = 99, ThumbnailSize = double.NaN,
    SidebarWidth = double.PositiveInfinity, LastFolder = "relative", SortField = (MediaSortField)99 }.Normalize();
Check(sanitized.Width == 320 && sanitized.Height == 4320 && sanitized.Theme == 0 && sanitized.ThumbnailSize == 240
    && sanitized.SidebarWidth == 260 && sanitized.LastFolder is null && sanitized.SortField == MediaSortField.Name, "非法设置应被校正");
Check(new AppSettings { LastFolder = "E:\\invalid\0path" }.Normalize().LastFolder is null, "非法绝对路径不能进入启动导航");
Check(new AppSettings { DisplayMode = (GalleryDisplayMode)99 }.Normalize().DisplayMode == GalleryDisplayMode.Masonry,
    "非法视图模式应回退瀑布流");
var viewSettings = savedSettings with { DisplayMode = GalleryDisplayMode.Filmstrip, ShowFileNames = false };
viewSettings = viewSettings with { Filter = MediaFilter.Videos, ShowSubfolders = false, SortField = MediaSortField.Taken };
Check(new AppSettings { Filter = (MediaFilter)99 }.Normalize().Filter == MediaFilter.All, "非法筛选应回退所有媒体");
Check(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"TreeShowModifiedTime\":true}") is
    { TreeShowModifiedTime: true, TreeTimeField: FolderTimeField.Modified }, "旧时间显示设置应继续显示修改时间");
Check(new AppSettings { TreeTimeField = (FolderTimeField)99 }.Normalize().TreeTimeField == FolderTimeField.Modified,
    "非法时间显示类型回退修改时间");
var timeEntry = new FolderEntry(@"C:\sample", "sample") { ShowModifiedTime = true, ModifiedTicks = new DateTime(2026, 2, 3).Ticks,
    CreatedTicks = new DateTime(2024, 5, 6).Ticks, TimeField = FolderTimeField.Created };
Check(timeEntry.ModifiedText == new DateTime(timeEntry.CreatedTicks!.Value, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
    && timeEntry.TimeLabel == "创建时间" && (timeEntry with { CreatedTicks = null }).ModifiedText == "创建时间未知",
    "时间显示按选择读取正确日期，缺失时不借用另一种时间");
var timeSettings = savedSettings with { TreeShowModifiedTime = true, TreeTimeField = FolderTimeField.Created };
Check(await settingsStore.SaveAsync(timeSettings) && (await settingsStore.LoadAsync()).TreeTimeField == FolderTimeField.Created,
    "文件树创建时间显示选择可持久化");
Check(await settingsStore.SaveAsync(viewSettings) && SameSettings(await settingsStore.LoadAsync(), viewSettings),
    "视图模式和文件名开关必须跨启动保存");
Check((await Task.WhenAll(Enumerable.Range(0, 10).Select(i => settingsStore.SaveAsync(savedSettings with { Width = 1000 + i })))).All(ok => ok), "并发保存不能互相覆盖临时文件或写出半份 JSON");
Check(await settingsStore.SaveAsync(savedSettings) && SameSettings(await settingsStore.LoadAsync(), savedSettings), "退出时最后保存必须生效");
Check(!Directory.EnumerateFiles(Path.GetDirectoryName(settingsPath)!, "*.tmp").Any(), "原子保存不应遗留临时文件");
Check(!await new AppSettingsStore(Path.GetDirectoryName(settingsPath)!).SaveAsync(savedSettings), "设置目标不可写时应安全返回失败");

var random = new Random(42);
var ratios = Enumerable.Range(0, 10_000).Select(_ => Math.Exp(random.NextDouble() * 6 - 3)).ToArray();
ratios[0] = double.NaN;
ratios[1] = 0;
ratios[2] = double.PositiveInfinity;
ratios[3] = -1;
int ratioReads = 0;
var timer = Stopwatch.StartNew();
foreach (var width in new[] { 0d, 100, 800, 1400, 3840 })
{
    var layout = new MasonryGeometry(ratios.Length, width, 240, 6, i => { ratioReads++; return ratios[i]; }, 6, 22);
    Check(layout.Bounds.All(b => double.IsFinite(b.Bottom) && b.Width >= 0 && b.Height >= 0 && b.X >= 0 && b.X + b.Width <= width + 0.001), "布局坐标越界或出现非有限值");
    Check(layout.Bounds.Zip(layout.Bounds.Skip(1)).All(pair => pair.First.Y <= pair.Second.Y), "最短列布局的顶部应保持单调");
    foreach (var column in layout.Bounds.GroupBy(b => b.X))
        Check(column.Zip(column.Skip(1)).All(pair => pair.First.Bottom <= pair.Second.Y), "列内图块重叠");
    Check(Math.Abs(layout.ExtentHeight - layout.Bounds.Max(b => b.Bottom)) < 0.001, "滚动范围错误");
    int readsAfterBuild = ratioReads;
    for (int i = 0; i < 200; i++)
    {
        double y = random.NextDouble() * (layout.ExtentHeight + 1000) - 500;
        double height = random.Next(1, 2000);
        var expected = Enumerable.Range(0, ratios.Length).Where(j => layout.Bounds[j].Y < y + height && layout.Bounds[j].Bottom > y);
        Check(layout.GetVisibleIndices(y, height).SequenceEqual(expected), "可见区查询漏掉长图或返回错误索引");
    }
    Check(ratioReads == readsAfterBuild, "滚动不能重新读取宽高比");
    Check(!layout.GetVisibleIndices(0, 0).Any(), "零高度视口应为空");
}
foreach (var width in new[] { 0d, 100, 800, 1400, 3840 })
{
    var rows = MasonryGeometry.Justified(ratios.Length, width, 180, 6, i => ratios[i], 6, 22);
    Check(rows.Bounds.All(b => double.IsFinite(b.Bottom) && b.Width >= 0 && b.Height >= 0 && b.X >= 0), "自适应行坐标出现非有限值");
    var grouped = rows.Bounds.Select((b, i) => (b, i)).GroupBy(t => t.b.Y).ToList();
    Check(grouped.All(row => row.Select(t => t.b.Height).Distinct().Count() == 1), "自适应行内图块必须等高");
    Check(grouped.Zip(grouped.Skip(1)).All(pair => Math.Abs(pair.First.First().b.Bottom + 6 - pair.Second.Key) < 0.001), "自适应行之间应只隔一个间距");
    if (width >= 800)
    {
        Check(grouped.SkipLast(1).All(row => Math.Abs(row.Last().b.X + row.Last().b.Width - width) < 0.01), "除最后一行外每行都要铺满宽度");
        Check(grouped.SkipLast(1).All(row => row.Zip(row.Skip(1)).All(p => Math.Abs(p.First.b.X + p.First.b.Width + 6 - p.Second.b.X) < 0.01)),
            "自适应行内图块应只隔一个间距");
        var row0 = grouped[1].ToList();
        double imageHeight = row0[0].b.Height - 12 - 22;
        Check(row0.SkipLast(1).All(t => Math.Abs((t.b.Width - 12) / imageHeight - Math.Clamp(double.IsFinite(ratios[t.i]) && ratios[t.i] > 0 ? ratios[t.i] : 1, 0.25, 4)) < 0.001),
            "自适应行必须保持图片原始比例");
    }
    for (int i = 0; i < 100; i++)
    {
        double y = random.NextDouble() * (rows.ExtentHeight + 1000) - 500;
        double height = random.Next(1, 2000);
        var expected = Enumerable.Range(0, ratios.Length).Where(j => rows.Bounds[j].Y < y + height && rows.Bounds[j].Bottom > y);
        Check(rows.GetVisibleIndices(y, height).SequenceEqual(expected), "自适应行的可见区查询错误");
    }
}
var lastRow = MasonryGeometry.Justified(3, 1200, 200, 10, _ => 1.5);
Check(lastRow.Bounds.All(b => Math.Abs(b.Height - 200) < 0.001) && lastRow.Bounds[^1].X + lastRow.Bounds[^1].Width < 1200,
    "未排满的最后一行保持目标高度，不能拉伸铺满");
var empty = new MasonryGeometry(0, 800, 240, 6, _ => throw new InvalidOperationException());
Check(new MasonryGeometry(1, 240, 240, 8, _ => 2, 6, 22).Bounds[0].Height == 148,
    "标题行和内边距必须计入卡片高度，不能压缩图片比例");
Check(empty.ExtentHeight == 0 && !empty.GetVisibleIndices(0, 900).Any(), "空布局错误");
var large = Path.Combine(workspace, "work", "validation", "large");
if (Directory.Exists(large))
{
    var scanTimer = Stopwatch.StartNew();
    var largeItems = FolderScanner.Scan(large);
    Check(largeItems.Count == 10_000, "一万张图的测试目录扫描数量错误");
    Console.WriteLine($"Scan: {largeItems.Count:N0} files, {scanTimer.ElapsedMilliseconds} ms");
    var searchIndex = new MediaSearchIndex(Path.Combine(fixture, "large-search.idx"));
    scanTimer.Restart();
    await searchIndex.BuildAsync([large], default);
    long indexMs = scanTimer.ElapsedMilliseconds;
    scanTimer.Restart();
    var allSearch = await searchIndex.SearchAsync("type:photo", false, default);
    Check(allSearch.Total == 10_000 && allSearch.Items.Count == 300, "万图搜索必须正确计数并限制返回数量");
    Console.WriteLine($"Search index: 10,000 entries in {indexMs} ms; query {scanTimer.ElapsedMilliseconds} ms (local fixture only).");
}
Console.WriteLine($"PASS: {checks} checks; geometry correctness suite {timer.ElapsedMilliseconds} ms (includes brute-force oracle).");
var mediaFixtures = Path.Combine(workspace, "work", "validation", "media");
if (Directory.Exists(mediaFixtures))
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    foreach (var (name, ratio) in new[] { ("image1.jpg", 1600d / 900), ("image2.jpg", 720d / 1280), ("image10.jpg", 1d), ("motion.gif", 1.5d) })
    {
        var path = Path.Combine(mediaFixtures, name);
        var actual = await MediaProbe.TryGetAspectRatioAsync(path, MediaKind.Image, timeout.Token);
        Check(actual is double result && Math.Abs(result - ratio) < 0.001, $"媒体宽高比错误：{name}");
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(timeout.Token);
        using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 240, ThumbnailOptions.ResizeThumbnail).AsTask(timeout.Token);
        Check(thumbnail is not null && thumbnail.Size > 0 && thumbnail.Type == ThumbnailType.Image, $"系统缩略图不可用：{name}");
        if (thumbnail is not null)
        {
            var decoder = await BitmapDecoder.CreateAsync(thumbnail).AsTask(timeout.Token);
            Check(decoder.PixelWidth > 0 && decoder.PixelHeight > 0, $"缩略图无法解码：{name}");
        }
    }
    Check(await MediaProbe.TryGetAspectRatioAsync(Path.Combine(mediaFixtures, "broken.png"), MediaKind.Image, timeout.Token) is null, "损坏图片应返回空宽高比");
    Check(await MediaProbe.TryGetAspectRatioAsync(Path.Combine(mediaFixtures, "missing.png"), MediaKind.Image, timeout.Token) is null, "不存在的媒体应返回空宽高比");
    foreach (var name in new[] { "video.mp4", "video.mov" })
    {
        var ratio = await MediaProbe.TryGetAspectRatioAsync(Path.Combine(mediaFixtures, name), MediaKind.Video, timeout.Token);
        Check(ratio is > 0, $"视频元数据不可用：{name}");
    }
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    bool observedCancellation = false;
    try { await MediaProbe.TryGetAspectRatioAsync(Path.Combine(mediaFixtures, "image1.jpg"), MediaKind.Image, canceled.Token); }
    catch (OperationCanceledException) { observedCancellation = true; }
    Check(observedCancellation, "媒体探测必须传播取消");
    var dateFixture = Directory.CreateDirectory(Path.Combine(fixture, "dates")).FullName;
    foreach (var (name, year) in new[] { ("later.jpg", 2023), ("earlier.jpg", 2020) })
    {
        var path = Path.Combine(dateFixture, name);
        File.Copy(Path.Combine(mediaFixtures, "image1.jpg"), path);
        var photo = await StorageFile.GetFileFromPathAsync(path);
        await photo.Properties.SavePropertiesAsync(new Dictionary<string, object>
        {
            ["System.Photo.DateTaken"] = new DateTimeOffset(year, 4, 5, 12, 0, 0, TimeSpan.Zero),
        });
    }
    var dated = await MediaDates.ReadAsync(FolderScanner.Scan(dateFixture), timeout.Token);
    Check(dated.All(file => file.TakenTicks is > 0), "必须读取真实 JPEG EXIF 拍摄日期");
    Check(MediaSorter.Sort(dated, file => file, MediaSortField.Taken, false).Select(file => file.Name)
        .SequenceEqual(new[] { "earlier.jpg", "later.jpg" }), "真实拍摄日期应决定排序顺序");
    var stale = dated.Single(file => file.Name == "earlier.jpg");
    File.Copy(Path.Combine(mediaFixtures, "image2.jpg"), stale.Path, true);
    File.SetLastWriteTimeUtc(stale.Path, DateTime.UtcNow.AddSeconds(10));
    var replacedDate = await MediaDates.ReadAsync(FolderScanner.Scan(dateFixture), timeout.Token);
    Check(replacedDate.Single(file => file.Name == "earlier.jpg").TakenTicks is null,
        "覆盖成无拍摄日期文件后，不能继续命中旧日期缓存");
    bool dateCanceled = false;
    try { await MediaDates.ReadAsync(dated, canceled.Token); }
    catch (OperationCanceledException) { dateCanceled = true; }
    Check(dateCanceled, "拍摄日期读取必须响应取消");
    Console.WriteLine($"PASS: {checks} total checks including Windows media metadata and system image thumbnails.");
}
Console.WriteLine("These checks do not measure WinUI scrolling, decoder memory, GIF animation, or video playback.");
