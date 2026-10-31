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
var fixture = Path.Combine(workspace, "work", "validation", "corechecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
int checks = 0;
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
Check(previews.Count == 4 && previews.All(media.Contains), "封面候选应限量且跳过隐藏、系统及非媒体文件");
Check(FolderScanner.FindFolderPreviews(Path.Combine(fixture, "folder2")).Single().Name == "nested.jpg", "图片文件夹封面候选错误");
Check(FolderScanner.FindFolderPreviews(Path.Combine(fixture, "empty")).Count == 0, "空目录不应有封面候选");
Check(FolderScanner.FindFolderPreviews(Path.Combine(fixture, "missing")).Count == 0, "失效目录封面应安全回退");
File.WriteAllText(Path.Combine(fixture, "folder10", "video.mp4"), "fixture");
Check(FolderScanner.FindFolderPreviews(Path.Combine(fixture, "folder10")).Single().Kind == MediaKind.Video, "纯视频目录也应有封面候选");
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

MediaFile SortFile(string name, long length, long ticks) => new(Path.Combine(fixture, name), MediaKind.Image)
{ Version = new FileVersion(Path.Combine(fixture, name), length, ticks) };
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
    Check(MediaSorter.Sort(sortable.Append(missingMetadata), f => f, MediaSortField.Size, descending)[^1] == missingMetadata,
        "缺少元数据的文件始终排在这一组末尾");
}
Check(NavigationGesture.Classify(100, 5) == NavigationAction.Next && NavigationGesture.Classify(-100, 5) == NavigationAction.Previous, "左右手势映射错误");
Check(NavigationGesture.Classify(5, -100) == NavigationAction.Parent && NavigationGesture.Classify(5, 100) == NavigationAction.Back, "上下手势映射错误");
Check(NavigationGesture.Classify(20, 0) == NavigationAction.None && NavigationGesture.Classify(90, 90) == NavigationAction.None
    && NavigationGesture.Classify(double.NaN, 100) == NavigationAction.None, "抖动、斜向和无效坐标不应执行手势");

var settingsPath = Path.Combine(fixture, "settings", "settings.json");
var settingsStore = new AppSettingsStore(settingsPath);
Check(await settingsStore.LoadAsync() == new AppSettings(), "首次启动应使用默认设置");
var savedSettings = new AppSettings { Width = 1100, Height = 700, Maximized = true, LastFolder = navRoot, Theme = 2,
    SidebarWidth = 300, ThumbnailSize = 320, SortField = MediaSortField.Modified, Descending = true };
Check(await settingsStore.SaveAsync(savedSettings) && await settingsStore.LoadAsync() == savedSettings, "所有设置必须完整往返保存");
File.WriteAllText(settingsPath, "{broken json");
Check(await settingsStore.LoadAsync() == new AppSettings(), "损坏设置不能阻止启动");
var sanitized = new AppSettings { Width = -1, Height = 99999, Theme = 99, ThumbnailSize = double.NaN,
    SidebarWidth = double.PositiveInfinity, LastFolder = "relative", SortField = (MediaSortField)99 }.Normalize();
Check(sanitized.Width == 800 && sanitized.Height == 4320 && sanitized.Theme == 0 && sanitized.ThumbnailSize == 240
    && sanitized.SidebarWidth == 260 && sanitized.LastFolder is null && sanitized.SortField == MediaSortField.Name, "非法设置应被校正");
Check(new AppSettings { LastFolder = "E:\\invalid\0path" }.Normalize().LastFolder is null, "非法绝对路径不能进入启动导航");
Check(new AppSettings { DisplayMode = (GalleryDisplayMode)99 }.Normalize().DisplayMode == GalleryDisplayMode.Masonry,
    "非法视图模式应回退瀑布流");
var viewSettings = savedSettings with { DisplayMode = GalleryDisplayMode.Filmstrip, ShowFileNames = false };
Check(await settingsStore.SaveAsync(viewSettings) && await settingsStore.LoadAsync() == viewSettings,
    "视图模式和文件名开关必须跨启动保存");
Check((await Task.WhenAll(Enumerable.Range(0, 10).Select(i => settingsStore.SaveAsync(savedSettings with { Width = 1000 + i })))).All(ok => ok), "并发保存不能互相覆盖临时文件或写出半份 JSON");
Check(await settingsStore.SaveAsync(savedSettings) && await settingsStore.LoadAsync() == savedSettings, "退出时最后保存必须生效");
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
    Console.WriteLine($"PASS: {checks} total checks including Windows media metadata and system image thumbnails.");
}
Console.WriteLine("These checks do not measure WinUI scrolling, decoder memory, GIF animation, or video playback.");
