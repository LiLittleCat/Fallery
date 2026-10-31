# Fallery（瀑廊）

Windows 上的瀑布流图片浏览器和视频播放器。Fallery = Falls + Gallery。
功能参考 macOS 应用 [FlowVision](https://github.com/netdcy/FlowVision)（GPL-3.0），但代码是从头写的。不要逐行翻译 FlowVision 的 Swift 代码，否则本项目必须以 GPL-3.0 开源。

完整路线图和验收标准见 [PLAN.md](PLAN.md)。动手前先读它，确认当前处在哪个阶段。

## 当前状态

- 骨架代码是在 macOS 上写的，2026-10-31 已在 Windows 10 x64 首次构建通过（0 错误、0 警告）并启动。后续问题按报错修，不要推翻整体结构。
- 进度：阶段 0 的 SDK、构建和启动已验证；阶段 1–3 已有初版，完整交互和性能验收待完成（自动截图及点击工具暂不可用，见 PLAN.md）。阶段 4–6 还没开始。

## 技术栈

- C# 14 / .NET 10，WinUI 3（Windows App SDK 1.8+），CommunityToolkit.Mvvm 8.4
- UI 扩展按需使用 Windows Community Toolkit；已接入 `CommunityToolkit.WinUI.Controls.Sizers` 8.2.251219（侧栏 `GridSplitter`）。
- 开发期是非打包（`WindowsPackageType=None`）、自包含。阶段 5 做文件关联时再决定是否改 MSIX，在此之前不要改
- 最低 Windows 10 19041，平台 x64 / ARM64

## 常用命令

```
dotnet restore
dotnet build -p:Platform=x64        # ARM 电脑用 -p:Platform=ARM64
dotnet run -p:Platform=x64
```

改完代码必须编译通过才算完成。涉及界面的改动，要运行程序实际点一遍。

## 目录结构

```
App.xaml(.cs)              入口，创建 MainWindow
MainWindow.xaml(.cs)       界面、文件树、导航、查看器、键盘处理（目前都在 code-behind 里）
Layouts/MasonryLayout.cs   瀑布流虚拟化布局（VirtualizingLayout），项目核心
Models/MediaItem.cs        单个图片/视频：宽高比、缩略图加载和释放
Models/FolderEntry.cs      文件树和面包屑节点
Services/FolderScanner.cs  目录扫描、扩展名过滤、自然排序（StrCmpLogicalW）
Services/MediaProbe.cs     只读文件头获取宽高比
Assets/Fallery.ico         程序图标（多尺寸），由 logo/build_ico.py 生成
logo/                      图标源文件和设计说明，见 logo/README.md
```

## 已经定下来的设计（改动前先和用户确认）

- **瀑布流**：`ScrollViewer` + `ItemsRepeater` + 自定义 `MasonryLayout`。不要换成 `GridView` / `ItemsView` 或一次性渲染全部元素的方案，目标是上万张图依然流畅。
- **布局缓存**：`MasonryLayout` 缓存所有项的位置，只有宽度、数量或调用 `Refresh()` 后才重算；滚动时只筛选可见区间。不要在滚动路径上加 O(n) 的 WinRT 调用。
- **缩略图**：用 `StorageFile.GetThumbnailAsync`（走系统缩略图缓存，视频也能用）。在 `ElementPrepared` 时加载，`ElementClearing` 时释放。
- **宽高比**：后台并发探测，每 200 项调用一次 `Refresh()`。探测完成前按 1:1 占位。
- **文件树**：`TreeView` 懒加载，节点展开时才在后台列子目录。
- **侧栏宽度**：使用 Toolkit `GridSplitter`，默认 260，范围 160–480（XAML 逻辑像素）；拖动与瀑布流联动尚待实际验收，见 PLAN.md。
- **外观主题**：工具栏提供跟随系统、亮色、暗色，启动默认跟随系统（暂不保存选择）。使用 WinUI `TitleBar` + `ExtendsContentIntoTitleBar`，标题和背景跟随根布局的主题资源；系统窗口按钮通过 `AppWindow.TitleBar.PreferredTheme` 同步，保留系统拖动和窗口控制。不再直接调用 DWM 修改标题栏。查看器保持深色并位于标题栏下方。
- **查看器**：覆盖在主窗口上的层，不开新窗口，也不调用外部程序。图片用 `ScrollViewer(ZoomMode)` + `Image`，视频暂时用 `MediaPlayerElement`。
- **播放内核**：阶段 6 会换成 LibVLCSharp 或 libmpv，并统一包在 `IPlayer` 接口后面。在那之前不要引入这两个依赖。
- **启动顺序**（阶段 5）：从“打开方式”启动时先播放，扫描目录和展开文件树放到后台。

## 图标

- 程序图标是 `Assets/Fallery.ico`，csproj 的 `ApplicationIcon` 用它设置 exe 图标，`MainWindow` 构造函数里用 `AppWindow.SetIcon` 设置窗口和任务栏图标。WinUI `TitleBar.IconSource` 使用 `Assets/FalleryTitleBar.scale-*.png`（逻辑 URI 不带 scale），按 100%–400% 缩放选取原生尺寸；不要直接加载 ICO，图像控件只读取第一帧会导致放大模糊。两种资源由同一脚本、同一三列 SVG 生成。
- 按用户最新要求，所有尺寸统一使用光影三列版，包括 16/20/24px 的标题栏和任务栏图标。`logo/build_ico.py` 从 `fallery-icon-fluent.svg` 生成全部 ICO 条目，不再使用两列版。
- 不要手动编辑 `.ico` 或 `logo/final/` 里的文件，它们都由 `logo/` 下的脚本生成。Windows 上没有 `uv` 和 geometric-logo skill 时，直接使用现有文件即可。

## 编码约定

- 风格跟随现有代码：文件作用域命名空间、`Nullable` 开启、私有字段 `_camelCase`、集合表达式 `[]`。
- 注释用中文，只写“为什么”，不写代码本身已经表达清楚的内容。界面文案用中文。
- **不要阻塞 UI 线程**：文件系统操作放到 `Task.Run`，切换目录时取消上一次加载（`CancellationTokenSource`）。
- `BitmapImage` 等 UI 对象只能在 UI 线程创建。`await` 之后会回到 UI 线程，`Parallel.ForEachAsync` 的回调里不能碰 UI。
- 只有事件处理器可以写 `async void`，其他异步方法一律返回 `Task`。
- XAML 优先用 `x:Bind`；按钮样式用 `MainWindow.xaml` 里定义的 `IconButtonStyle`。
- 阶段 4 之前允许把逻辑写在 code-behind 里；阶段 4 再抽成 MVVM（`GalleryViewModel` / `ViewerViewModel`）。不要提前大规模重构。
- 不要擅自加新的 NuGet 依赖，需要时先说明理由。

## 尚未验证的点（遇到问题优先怀疑这些）

- `ItemsRepeater.Layout` 里 `MasonryLayout` 的 `x:Bind DesiredColumnWidth` 绑定是否生效
- `ElementPrepared` 时 `DataContext` 是否已经设置（目前用 `args.Index` 取数据，规避了这个问题）
- 非打包模式下 `StorageFile.GetFileFromPathAsync` 和 `GetThumbnailAsync` 的表现
- 查看器 `FitImageToViewport` 在首次显示时的尺寸时机
- HEIC / HEVC 需要系统扩展，缺少时应该显示占位，不能崩溃

## 完成一项任务时

1. `dotnet build` 无错误、无新增警告
2. 实际运行，验证 PLAN.md 中对应阶段的验收标准
3. 在 PLAN.md 里勾掉完成的条目；如果设计有变化，同步更新本文件的“已经定下来的设计”
