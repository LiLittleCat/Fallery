<p align="center">
  <img src="logo/final/fallery-icon-fluent-1024.png" width="96" alt="Fallery 图标">
</p>

# Fallery

**Fallery = Falls + Gallery。** 一款 Windows 瀑布流媒体浏览器，支持图片查看、视频和音频播放。

[English](README.md) · 简体中文

![深色瀑布流视图](docs/screenshots/masonry-dark-beaa11082d55.png)

## 主要功能

- **浏览：**瀑布、自适应、网格、大图与缩略图、列表视图，支持文件夹封面、收藏与导航历史。
- **看图：**缩放、拖动、GIF、全屏与照片信息；双击放大，再双击适应窗口。
- **播放：**视频和音频、播放列表、倍速、续播与 A–B 循环。
- **整理：**搜索、排序、筛选、复制、移动、重命名、删除及相同图片查找。
- **个性化：**明暗主题、缩略图大小、文件夹排除、多窗口和中英文即时切换。

## 安装

**系统要求：**Windows 10 2004 / 内部版本 19041 及以上，或 Windows 11；提供 **x64** 与 **ARM64** 构建。

从 [Releases](https://github.com/LiLittleCat/Fallery/releases) 下载对应架构的 `*-Setup.exe`，无需额外安装 .NET。

免安装版下载 `*-Portable.zip`，解压后运行。

安装版可在**设置 → 应用 → 版本与更新**中检查更新。

版本变化见[更新日志](CHANGELOG.md)。

## 媒体格式

| 类型 | 示例 |
| --- | --- |
| 图片 | JPEG、PNG、GIF、BMP、WebP、TIFF、HEIC/HEIF、AVIF |
| 视频 | MP4、MOV、MKV、AVI、WMV、WebM |
| 音频 | MP3、WAV、FLAC、M4A、AAC、WMA、OGG、Opus |

格式支持取决于已安装的 Windows 解码器，HEVC、AV1 可能需要扩展。Fallery 不内置解码器，不支持字幕。

## 常用快捷键

| 操作 | 快捷键 |
| --- | --- |
| 打开文件夹 / 新建窗口 | `Ctrl+O` / `Ctrl+N` |
| 编辑地址 / 全电脑搜索 | `Ctrl+L` 或 `Alt+D` / `Ctrl+F` |
| 后退 / 前进 / 上级 | `Alt+←` / `Alt+→` / `Alt+↑` |
| 浏览时进入上级 / 后退 | `W` / `S` |
| 上一个 / 下一个媒体目录 | `A` / `D` |
| 刷新目录 | `F5` |
| 全屏 / 退出全屏或查看器 | `F11` / `Esc` |
| 适应图片 / 放大 / 缩小 | `Ctrl+0` / `Ctrl+=` / `Ctrl+-` |
| 播放暂停 / 静音 | `Space` / `M` |
| 详细信息 / 播放列表 | `I` / `L` |
| 删除 / 永久删除 | `Delete` / `Shift+Delete`，均需确认 |

在浏览区按住鼠标右键拖动，也可切换目录。

## 从源码构建

需要 Windows 和 [global.json](global.json) 指定的 .NET SDK，使用 C# 与 WinUI 3 构建。

```powershell
dotnet restore
dotnet build -p:Platform=x64
dotnet run -p:Platform=x64
```

ARM64 构建将 `x64` 改为 `ARM64`。

生成安装包和更新文件：

```powershell
./tools/Build-Release.ps1 -Version 0.1.0 -Architecture x64
```

## 本地数据

| 构建类型 | 数据目录 |
| --- | --- |
| stable 正式安装版 | `%LOCALAPPDATA%\Fallery` |
| preview 预览版 | `%LOCALAPPDATA%\Fallery.Preview` |
| 本地开发版 | `%LOCALAPPDATA%\Fallery.Dev` |

这些目录保存设置、续播位置、搜索索引和缩略图，免安装版也使用这些目录。媒体文件保留在原来的位置。

## 致谢

- 界面使用 Windows App SDK / WinUI 3 与 .NET Community Toolkit。
- 图标使用 Hugeicons Stroke Rounded，见[字体许可](Assets/Fonts/LICENSE-Hugeicons.txt)。
- 安装和更新使用 [Velopack](https://github.com/velopack/velopack)。

## 许可证

[MIT](LICENSE) © 2026 Yi Liu。
