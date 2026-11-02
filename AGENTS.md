# Fallery（瀑廊）开发规则

Windows 图片浏览器与影音播放器，C# / .NET 10、WinUI 3、CommunityToolkit.Mvvm；最低 Windows 10 19041，支持 x64 / ARM64。项目采用 MIT 许可证。

## 开始工作

- 先读本地 `PLAN.md` 的当前进度及相关验收项；历史实现不等于已通过人工验收。
- 修改具体功能前，按需查阅 [详细设计与验收记录](docs/DEVELOPMENT-NOTES.md) 的对应章节。保留已确定的行为；改变设计前确认用户是否已经授权，不重复询问已有要求。
- 构建、安装、升级和数据目录见 [DEPLOYMENT.md](DEPLOYMENT.md)；测试入口见 [Tests/README.md](Tests/README.md)。
- 保留现有结构和用户未提交的改动，避免无关重构。不擅自增加 NuGet 依赖；确有需要先说明理由。

## 构建与验证

```powershell
dotnet restore
dotnet build -p:Platform=x64
dotnet run -p:Platform=x64
dotnet run --project Tests/CoreChecks/CoreChecks.csproj -- .
dotnet run --project Tests/LanguageChecks/LanguageChecks.csproj -- .
```

- ARM64 使用 `-p:Platform=ARM64`；SDK 版本以 `global.json` 为准。
- 代码修改必须编译通过且无新增警告；按改动范围执行相关检查，纯文档修改检查链接及 `git diff --check`。
- **不使用 Computer Use**。界面改动编译后启动程序，提供具体人工验收步骤；隐藏宿主检查和截图不能替代真实交互验收。
- 自动检查使用隔离数据，从启动起保持 `Muted=true`、`Volume=0`，不得修改用户应用或系统音量。完成后退出测试进程，不对用户文件执行测试删除。
- 完成后更新 `PLAN.md` 的实际进度；详细设计更新到对应文档，不把逐项变更日志追加到本文件。

## 架构与兼容性

- 保持非打包、自包含（`WindowsPackageType=None`），用 Velopack 构建安装和更新包，不改 MSIX。开发、正式和预览版的数据与进程身份分离。
- 一进程、同一 UI 线程管理多个独立窗口；窗口分别持有导航、选择、查看器和播放器。公共缓存及持久化服务共享，最后一个窗口关闭才清理公共内存缓存。
- `GalleryViewModel` 管扫描、导航、排序；`ViewerViewModel` 管播放列表与加载取消；窗口保留原生控件资源及交互接线。
- 保留 `ScrollViewer + ItemsRepeater + MasonryLayout` 虚拟化；禁止改成一次性创建全部元素。滚动路径不加 O(n) WinRT 调用。
- 卡片显式 Bind/清理订阅与图片引用；布局负责回收实际 UIElement，不能只清索引。Arrange 只使用 Measure 已准备的元素。
- 文件和缓存以完整路径、大小、UTC 修改时间校验。保留有界 LRU、并发限制、取消与代次保护；清缓存后旧任务不能写回。不引入 SQLite。
- 保留五种视图与旧枚举值，持久化枚举只能追加；设置键与旧数据兼容，使用现有原子写入流程。
- 播放器经 `IPlayer` 使用 Windows 系统内核，不捆绑解码器；格式支持取决于系统。音频共享播放操作，视频取帧、截图、旋转仍只用于视频。
- 文件操作保留目标确认、取消、版本校验及失败处理；回收站失败不能静默降级为永久删除。递归扫描不跟随联接或符号链接。

## 界面与编码

- 沿用现有主题、图标与控件样式；图标资源由 `tools/build_icons.py` 生成，保留第三方许可。
- 所有界面文案进入 `Strings/zh-CN.json` 与 `Strings/en-US.json`，两种语言同步维护。语言即时切换、取消设置回滚；不翻译文件名、路径、搜索语法或内部标识。
- 文件作用域命名空间、Nullable、`_camelCase` 私有字段、集合表达式 `[]`；中文注释解释原因。
- 文件系统操作放后台并支持取消；UI 对象只在 UI 线程创建和访问。只有事件处理器使用 `async void`，其他异步方法返回 `Task`。
- XAML 优先 `x:Bind`；可回收控件状态仍需显式绑定和清理。

## README 展示图

- 英文 `README.md` 为主，链接 `README.zh-CN.md`。界面、主题或文案变化影响配图时重新生成并同步两份 README。
- 用 `tools/Generate-ReadmeSamples.py` 生成 24 张样例；构建 ExperienceChecks 后，以 `FALLERY_README_SCREENSHOTS=1` 运行。完整命令及检查步骤见 [截图维护说明](docs/screenshots/README.md)。
- 截图夹具 `Tests/ExperienceChecks/App.ReadmeScreenshots.cs` 在独立进程内渲染真实 WinUI XAML（`RenderTargetBitmap`），输出 `docs/screenshots/` 的三张 PNG；界面结构变化时同步维护夹具。
- 只使用生成器清单核验的样例、隔离设置及缓存，禁止个人图库、收藏、最近路径和桌面内容。样例及缓存留在忽略的 `work/readme-demo`。
- 检查报告 PASS 并逐张复核布局、语言、隐私与元数据；不把截图当作交互通过。公开 README 不加入样例生成或隐私说明段落。

## 提交规范

- 所有提交使用带 scope 的 Conventional Commits：`type(scope): description`，如 `feat(i18n): add live language switching`、`fix(viewer): preserve viewer state`、`docs(dev): simplify development guide`。scope 必填，描述实际改动的模块。
- 提交标题、正文和脚注只描述本项目的改动，不提已移除的外部参考项目、其作者或链接；提交中的新增文档、注释和其他文本同样遵守。
- 提交前检查完整提交消息与暂存差异，不只检查标题；保留真实第三方组件依法需要的许可声明。
- 未获授权不提交或推送；不得为清理文字擅自重写历史、变基已发布提交或强制推送。
