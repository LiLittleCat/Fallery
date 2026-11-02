# 构建、发版、更新与数据目录

更新时间：2026-11-02。已接入 Velopack 1.2.161，保持非打包 WinUI、自包含部署。源码与二进制发行使用同一公开仓库 LiLittleCat/Fallery，首个版本为 `v0.1.0`。

## GitHub Actions 发版

1. 将 `CHANGELOG.md` 的 `Unreleased` 归入本次版本与日期，更新比较链接及 `Fallery.csproj` 版本，提交并推送代码。普通 main 推送/PR 运行 `Build and checks`。Changelog 遵循 Keep a Changelog；构建及发行说明默认只提取本次版本，缺失、重复或空版本条目会拒绝打包。
2. 打开仓库 Actions → **Build release** → **Run workflow**，选择要构建的分支，填写三段版本号（例如 `0.1.0`）与 `stable`。预览版用 `0.2.0-preview.1` 与 `preview`，两者必须匹配。
3. 工作流先检查，再在 Windows runner 上分别构建 x64、ARM64。SDK 由 `global.json` 固定，NuGet 与本地 vpk 版本固定。两个架构全部成功后下载产物、校验提交和哈希，创建 `v<版本>` 的 **Release 草稿**。
4. 从 Actions artifacts 或 Release 草稿下载匹配机器架构的 `*-Setup.exe`，完成安装验收。审核更新内容与两个架构的完整资产后，在 GitHub Release 页面点击 **Publish release**。
5. 客户端只读取已发布版本；草稿不会推送给用户。已发布版本禁止覆盖，修复后提高版本号重新构建。同一提交/版本/通道的失败草稿可重跑上传；不同提交使用新的版本，或先人工处理旧草稿及标签。

工作流位于 `.github/workflows/ci.yml` 与 `.github/workflows/release.yml`，仅发版最后一个 job 拥有 `contents: write`。使用 GitHub 自动提供的 token，不需要把私人 token 写入代码。版本号通过环境变量传给验证过的参数，不拼进 shell 脚本。

客户端匿名读取公开仓库中已发布的 Releases，不需要用户提供 GitHub 凭据。未发布草稿不可用于更新；若仓库转为私有或网络受限，检查会失败，不会把 HTTP 404 解释为“已是最新”。不要把 token 打入安装包。

提交并推送后，在 Actions 页面确认远端工作流结果；普通推送不会创建发行版，发版需手动触发上述流程。

## 本地构建

需要 Windows、.NET SDK 10.0.403（允许同 feature band 的补丁升级）、网络访问 NuGet。脚本从项目根目录运行；vpk 为项目本地工具，不要求全局安装。

```powershell
# 先运行检查；每个架构单独构建。
./tools/Build-Release.ps1 -Version 0.1.0 -Architecture x64
./tools/Build-Release.ps1 -Version 0.1.0 -Architecture arm64

# 预览版拥有独立产品身份和数据目录。
./tools/Build-Release.ps1 -Version 0.2.0-preview.1 -Channel preview -Architecture x64
```

输出在 `work/releases/<通道>/<版本>/win-<架构>`。脚本拒绝非空输出目录，避免混入旧包；重试可指定新的 `-OutputRoot ./work/retry-releases`。每次 publish 都使用 `work/release-builds/<GUID>` 下的独立中间目录，不覆盖正在使用的开发输出。`-ReleaseNotes <文件>` 可指定本次更新说明，默认从 `CHANGELOG.md` 提取对应版本；`-SkipChecks` 仅供已经在前置 job 完成检查的场景。

| 产物 | 用途 |
| --- | --- |
| `Fallery.App-stable-win-x64-Setup.exe`（ARM64 同理） | 每用户安装包，首次安装或手动升级 |
| `*-Portable.zip` | 解压运行的程序，不启用应用内更新；数据仍写 LocalAppData，并非完全便携数据模式 |
| `*-full.nupkg` | 完整更新包，客户端下载安装；当前不生成差分包 |
| `releases.<通道>-win-<架构>.json` | 客户端更新清单，必须与包一起发布 |
| `RELEASES-*`、`assets.*.json` | vpk 生成的配套索引 |
| `build-*.json` | 版本、架构、产品、更新源、提交、工作区是否有未提交改动 |
| `SHA256SUMS-*.txt` | 产物与元数据的 SHA-256 校验和 |

正式默认产品 ID 是 `Fallery.App`，预览为 `Fallery.Preview.App`；版本号必须递增。架构通过 `stable-win-x64` / `stable-win-arm64` 等独立 feed 区分。编译使用显式 `FalleryChannel=stable/preview`，更新源通过 `FalleryUpdateSource` 写入程序集；不能靠 Debug/Release 判断是否正式发布。

脚本默认源为 `https://github.com/LiLittleCat/Fallery`。`-UpdateSource https://...` 可改成其他仓库首页或静态 HTTPS 目录，禁止 URL 中的凭据、查询参数及片段。单独运行 `dotnet publish` 只生成程序目录，不生成安装包，而且默认仍是开发版。

签名证书尚未配置，当前安装包未签名。接入签名时由构建环境提供 `VPK_SIGN_PARAMS` 或 `VPK_AZURE_TRUSTED_SIGN_FILE`，由 vpk 签署程序和安装器；证书、密码不提交仓库。当前无签名不是“已验证发布者”的状态。

## 本地校验和创建发行草稿

```powershell
$commit = git rev-parse HEAD
./tools/Publish-Release.ps1 -Version 0.1.0 -ArtifactsDirectory ./work/releases/stable/0.1.0 -Commit $commit -ValidateOnly

# 需要 GitHub CLI 登录；构建时源码工作区必须干净。
./tools/Publish-Release.ps1 -Version 0.1.0 -ArtifactsDirectory ./work/releases/stable/0.1.0 -Commit $commit
```

默认只创建草稿。明确需要直接发布时追加 `-Publish`；脚本仍先上传全部资产，成功后才发布。未提交源码构建的包允许 `-ValidateOnly` 和本地试用，但不允许发布，避免发行标签无法对应实际代码。脚本要求 x64/ARM64 来自同一提交，版本与通道匹配，所有哈希正确且文件名不重复；不修改仓库可见性。

不要只上传 Setup.exe：客户端更新依赖 feed 和 nupkg，两个架构的完整资产必须一起发布。

## 应用内更新行为

界面语言在设置中选择“简体中文 / English”，无需重启，所有窗口立即生效；完成保留，取消/Esc 恢复。语言代码保存在对应通道的 `settings.json` 中，升级时随设置保留。旧配置未包含语言字段时默认中文。

入口：**设置 → 版本与更新**。

- 显示当前版本、数据目录及发行页面；安装版可检查、下载、取消下载、重试。开发版和解压运行版提供说明，不访问更新源。
- 同一进程所有窗口共用更新状态，只运行一个检查/下载/安装准备任务。检查超时 45 秒；取消下载后可以重新下载。下载包由 Velopack 校验，验证产品 ID，不降级，架构与发行通道固定。
- 下载完成显示“重启并更新”，再次确认后才开始；启动时不自动应用已下载更新。已下载包在下次启动仍可被识别。
- 所有窗口空闲才允许重启；其他窗口正在复制、移动、删除、重命名、选择目录、关闭，或打开对话框时，先提示完成操作。
- 重启前阻止新输入/新窗口，停止播放，等待所有窗口续播和设置保存，备份这两类 JSON；保存或备份失败不启动更新器。更新器启动后等待进程正常退出，再替换程序并重启。启动更新器失败时窗口恢复交互，可重试。
- 更新不复制媒体库，不修改系统音量，不把 Windows 默认应用强行改成 Fallery。

安装/更新后调用通道对应的 Shell 注册，卸载前按 exe 路径归属取消注册；旧路径卸载不会删除已经指向新路径的入口。Velopack fast hooks 不允许 UI，注册异常写 `update-hooks.log`，可在设置中重新注册；不能依赖钩子失败取消整个安装流程。现有 `--register-shell` / `--unregister-shell` 仍可用，退出码 0 成功、1 失败、2 参数错误，开发版真实注册返回失败。

## 安装版、预览版与开发版共存

| 项目 | 正式 stable | 预览 preview | 本地开发 dev |
| --- | --- | --- | --- |
| 显示名称 | Fallery | Fallery Preview | Fallery Dev |
| 默认程序安装根 | `%LOCALAPPDATA%\Fallery.App` | `%LOCALAPPDATA%\Fallery.Preview.App` | 项目的 bin 输出目录 |
| 数据根 | `%LOCALAPPDATA%\Fallery` | `%LOCALAPPDATA%\Fallery.Preview` | `%LOCALAPPDATA%\Fallery.Dev` |
| 实况临时目录 | `%TEMP%\Fallery-Motion` | `%TEMP%\Fallery.Preview-Motion` | `%TEMP%\Fallery.Dev-Motion` |
| exe | Fallery.exe | Fallery.Preview.exe | Fallery.exe |
| Shell ProgID / verb | Fallery.Media / Fallery.Open | Fallery.Preview.Media / Fallery.Preview.Open | 不注册 |
| 更新 | stable 对应架构 | preview 对应架构 | 禁用 |

普通 `dotnet build/run` 即使指定 Release 也属于 dev。旧 `%LOCALAPPDATA%\Fallery` 原样保留给正式版；开发版首次使用空设置，不自动导入或双向同步。需要复制偏好时退出全部副本，再显式复制 settings.json/resume.json 到目标数据根。

不同产品的数据根与注册身份已隔离。SDK 的单实例还按 exe 路径划分作用域，同一路径再次启动新建窗口；不同目录副本可以分别运行。同通道的多个安装/便携副本仍共享该通道的数据，不应同时写；x64 和 ARM64 也是同一个产品，安装包请按机器架构选择，不作为两份独立产品同时安装。

Windows App SDK 1.8 的路径作用域依据：[AppInstance](https://github.com/microsoft/WindowsAppSDK/blob/release/1.8-stable/dev/AppLifecycle/AppInstance.cpp)、[ComputeAppId](https://github.com/microsoft/WindowsAppSDK/blob/release/1.8-stable/dev/AppLifecycle/Association.cpp)。本项目未改 MSIX。

## 数据目录清单

以下相对路径位于上表对应的数据根。正式数据根为 `%LOCALAPPDATA%\Fallery`，开发根为同级 `Fallery.Dev`。Win+R 可输入 `%LOCALAPPDATA%\Fallery` 或 `%LOCALAPPDATA%\Fallery.Dev` 打开。

| 路径 | 内容和限制 | 清理影响 |
| --- | --- | --- |
| `settings.json` | 主题、窗口、视图、排序/筛选、收藏/排除目录、最近 20 个目录、最后位置、音量等 | 删除恢复默认偏好；详情和播放列表展开状态不恢复 |
| `resume.json` | 音视频续播位置，按路径/大小/修改时间匹配，最多 500 条 | 删除只丢续播位置 |
| `Search\media-v1.idx` | 路径、类型、隐藏标记，最多 100 万项，无媒体副本、无 SQLite | 可重建 |
| `Thumbnails\v1\*.thumb` | 压缩缩略图，512 MiB / 1 万条，单条超过 4 MiB 不保存 | 设置中清除缓存后会重新生成，不删媒体 |
| `errors.log` | 捕获到的未处理异常/堆栈，可能包含路径，无大小轮转；不是每次原生崩溃都可记录 | 退出后可清理 |
| `update-hooks.log` | 安装/更新/卸载期间 Shell 注册异常，无大小轮转 | 诊断用，退出后可清理 |
| `Backups\before-update-<UTC时间>-<GUID>` | 应用内重启更新前的 settings.json、resume.json、version.txt | 不复制索引/缓存；目前保留历史备份，用户可清理旧备份 |
| `settings.json.<GUID>.tmp` | 设置原子写入中间文件 | 正常清理，异常终止可能残留 |
| `resume.json.tmp`、`Search\media-v1.idx.tmp` | 续播/索引替换中间文件 | 成功后替换正式文件，异常终止可能残留 |
| `Thumbnails\v1\*.thumb.tmp` | 缩略图写入中间文件 | 正常清理，缓存初始化也清理遗留文件 |

数据根之外：

| 位置 | 用途 |
| --- | --- |
| 程序安装根的 `current` | 当前 exe、运行时和资源，更新时由 Velopack 替换，不能存放个人数据 |
| 程序安装根的 `packages`、`packages\VelopackTemp` | 更新包及更新临时文件，由 Velopack 管理 |
| 程序安装根的 `Update.exe`、版本/卸载元数据 | 更新器与安装管理，不要单独移动或删除 |
| `%LOCALAPPDATA%\velopack\velopack_<包ID>.log` | 已安装应用的 SDK 更新诊断；未检测到安装身份时为 `velopack.log`；该目录也可能有其他 Velopack 应用/安装工具日志，不要整目录清空 |
| 共存表中实况临时目录下的 `motion-<GUID>.mp4` | 实况 JPG 提取的临时视频，停止/切图/关闭尝试删除，下次提取清理超过一天的自有遗留文件 |
| `FALLERY_PLAYBACK_TRACE` 环境变量指定的文件 | 可选播放诊断，默认关闭；无固定默认路径，不记录媒体路径/内容，当前无轮转 |
| 原视频所在目录的 `<名称>_<时刻>.png` | 用户主动保存的截图，重名另生成名称；不是缓存 |
| 项目的 `bin` / `obj` / `work/release-builds` / `work/releases` / `work/distributions` / `work/validation` | 开发构建、打包和检查产物，不属于安装版用户数据 |

`%TEMP%` 由当前 Windows 用户环境决定，代码使用 `Path.GetTempPath()` 获取，不假定固定用户名或磁盘路径。Windows 系统缩略图缓存由系统管理，应用“清除缩略图缓存”不清它。普通浏览不复制媒体整库；实况临时片段是例外。显式复制/移动/重命名/删除/截图操作的目标是用户选择的媒体目录；回收站由系统管理。

Shell 注册位置（正式版示例，预览版将 Fallery 身份替换为 Fallery.Preview）：

```text
HKCU\Software\Classes\Applications\Fallery.exe
HKCU\Software\Classes\Fallery.Media
HKCU\Software\Classes\<扩展名>\OpenWithProgids  （Fallery.Media 值）
HKCU\Software\Classes\SystemFileAssociations\<扩展名>\shell\Fallery.Open
HKCU\Software\Classes\Directory\shell\Fallery.Open
HKCU\Software\Classes\Directory\Background\shell\Fallery.Open
HKCU\Software\Classes\Drive\shell\Fallery.Open
HKCU\Software\Fallery\Capabilities
HKCU\Software\RegisteredApplications  （Fallery 值）
```

另外，安装器维护对应产品的卸载注册信息和桌面/开始菜单快捷方式。取消注册只删除本应用项，不能删 Classes 或 RegisteredApplications 等共享根。

## 升级、卸载与验证范围

应用数据始终在整个安装根目录之外；更新替换程序，卸载默认保留数据。备份或恢复前退出所有同通道副本，重要数据是 settings.json/resume.json，缓存和索引可重建。应用内更新已加备份，但手动运行新安装器不经过应用的备份按钮，应自行备份。当前没有通用数据格式迁移/回滚系统，不能承诺任意旧版都能理解新版 JSON；本轮不更改现有 JSON 架构，客户端禁止自动降级。

2026-11-02 本地已验证：x64/ARM64 发布与安装包生成、两个架构共 16 个资产的发布前校验；1,952 项核心检查、35 项更新检查、39 项隐藏生产窗口/手势检查通过。更新检查包含真实 SDK 对隔离 feed 的下载、哈希校验、损坏包拒绝；未执行真实安装器或替换用户程序。

2026-11-02 已正式发布 [v0.1.0](https://github.com/LiLittleCat/Fallery/releases/tag/v0.1.0)，标签对应 `77fded19f9a65f2a835d6eee426de2f80adf8981`。[普通 CI](https://github.com/LiLittleCat/Fallery/actions/runs/41470420772) 构建零警告、零错误，核心检查、35 项更新检查、2,485 项语言检查及发布说明检查通过；[发布工作流](https://github.com/LiLittleCat/Fallery/actions/runs/41470421599) 完成 x64/ARM64 构建和草稿上传。16 个资产的 GitHub SHA-256 与构建清单一致；远程读取两份便携包确认实际程序架构、MIT 与图标许可证。两个架构的 Setup 与更新 feed 均已匿名验证可访问。

本次网络的匿名 GitHub REST API 达到共享出口限额，在线版本发现未完成端到端验收；直接下载与 feed 访问正常。客户端通过 GitHub API 发现版本，遇到限流需稍后重试或换正常网络，不应加入客户端令牌绕过。

仍需验收：干净 Windows 机器安装；ARM64 实机运行；安装 N 版后从已发布 N+1 版完成检查→下载→确认→所有窗口退出→替换→重启，确认设置/续播和打开方式；取消/断网/保存失败重试；卸载后数据保留。按项目要求不使用 Computer Use，真实界面由用户手动检查。

实现入口：`tools/Build-Release.ps1`、`tools/Publish-Release.ps1`、`tools/Get-ReleaseNotes.ps1`、`Program.cs`、`Services/AppIdentity.cs`、`Services/AppUpdateService.cs`、`Services/VelopackUpdates.cs`、`Services/VelopackUpdateBackend.cs`、`Services/UpdateDataBackup.cs`、`Views/MainWindow/MainWindow.Updates.cs`。

Velopack 官方参考：[启动与更新](https://docs.velopack.io/integrating/overview)、[安装布局](https://docs.velopack.io/packaging/installer)、[更新源](https://docs.velopack.io/integrating/update-sources)、[数据保留](https://docs.velopack.io/integrating/preserved-files)。
