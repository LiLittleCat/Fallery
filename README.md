<p align="center">
  <img src="logo/final/fallery-icon-fluent-1024.png" width="96" alt="Fallery icon">
</p>

# Fallery

**A Windows gallery for browsing folders, viewing images, and playing video and audio.**

English · [简体中文](README.zh-CN.md)

Fallery combines *Falls* and *Gallery*: a flowing view of your media, with familiar folder navigation and a built-in viewer. Built with C# / .NET 10 and WinUI 3.

![Fallery's dark masonry view](docs/screenshots/masonry-dark.png)

## What you can do

- **Browse your folders in five layouts:** masonry, justified rows, a uniform grid, a large image with a filmstrip, or a detailed file list.
- **Stay oriented:** an expandable folder tree, folder-cover previews, an Explorer-style address bar, favorites, recent locations, and back/forward history.
- **View images in place:** zoom, pan, fit to window, actual-size viewing, GIF playback, full screen, and available photo metadata. Double-click an image to zoom in; double-click again to fit.
- **Play video and audio:** playback speed, volume, resume positions, playlists, A–B repeat, and adjacent-media playback. Video also supports frame stepping, rotation, and screenshots.
- **Find and organize media:** sorting, type filters, local filename indexing, multi-selection, copy/cut/paste, rename, folder creation, and deletion with confirmation. Identical-image cleanup compares file names, sizes, and content; it is not a visual-similarity search.
- **Make it comfortable:** light, dark, or system appearance; adjustable thumbnail sizes and sidebar width; multiple windows; English and Simplified Chinese with live language switching.

The image viewer lives inside the main window. Large folders use virtualized layouts, bounded memory caches, and a disk thumbnail cache rather than creating a control for every file at once.

## A closer look

### Large image + filmstrip

Keep the folder tree and navigation visible while stepping through a collection.

![Large-image browsing with a bottom filmstrip](docs/screenshots/filmstrip-dark.png)

### Light theme · 简体中文

Switch languages in **Settings → General → Language** without restarting. All open windows update together; **Done** keeps the choice, and **Cancel** restores the previous language.

![Fallery's light justified layout with the Simplified Chinese interface](docs/screenshots/justified-light-zh.png)

## Install

**Requirements:** Windows 10 version 2004 / build 19041 or later, or Windows 11. Builds target **x64** and **ARM64**.

1. Open [Releases](https://github.com/LiLittleCat/Fallery/releases).
2. Download the `*-Setup.exe` for your architecture and run it. If no release is available, use the source-build instructions below.
3. Open a folder and start browsing. Installers are self-contained; you do not need the .NET SDK to run them.

Installation is per user. A `*-Portable.zip`, when provided, can be extracted and run, but its settings and cache still live in LocalAppData.

**Updates:** installed release builds expose **Settings → Version and updates**. You can check, download, cancel, and explicitly confirm a restart to apply an update. Development and extracted builds do not update themselves. Anonymous updates require an accessible, published release; a private repository is not an anonymous update source.

Current packaging does not configure a code-signing certificate. See [deployment and release instructions](DEPLOYMENT.md) for package details, signing, and the GitHub Actions workflow.

## Supported media

| Media | Examples |
| --- | --- |
| Images | JPEG, PNG, GIF, BMP, WebP, TIFF, HEIC/HEIF, AVIF |
| Video | MP4, MOV, MKV, AVI, WMV, WebM |
| Audio | MP3, WAV, FLAC, M4A, AAC, WMA, OGG, Opus |

Recognizing an extension does not guarantee that Windows can decode its contents. Fallery uses Windows imaging and media APIs and **does not bundle a codec pack**. HEVC, AV1, and some image or container formats may require Windows extensions or otherwise fail to open. Subtitle support is not implemented.

## Useful shortcuts

| Action | Shortcut |
| --- | --- |
| Open a folder / new window | `Ctrl+O` / `Ctrl+N` |
| Edit the address / search this computer | `Ctrl+L` or `Alt+D` / `Ctrl+F` |
| Back / forward / parent folder | `Alt+←` / `Alt+→` / `Alt+↑` |
| Parent / back while browsing | `W` / `S` |
| Previous / next media folder | `A` / `D` |
| Refresh folder | `F5` |
| Full screen / leave full screen or viewer | `F11` / `Esc` |
| Fit image / zoom in / zoom out | `Ctrl+0` / `Ctrl+=` / `Ctrl+-` |
| Play or pause / mute | `Space` / `M` |
| Details / playlist | `I` / `L` |
| Delete / permanently delete | `Delete` / `Shift+Delete` — confirmation required |

Shortcuts depend on focus and the active view. In the gallery, holding the right mouse button and dragging also supports folder-navigation gestures.

## Build from source

Use Windows and the .NET SDK pinned in [global.json](global.json), currently **10.0.403** with compatible patch roll-forward.

```powershell
dotnet restore
dotnet build -p:Platform=x64
dotnet run -p:Platform=x64

# Core checks and language resources
dotnet run --project Tests/CoreChecks/CoreChecks.csproj -- .
dotnet run --project Tests/LanguageChecks/LanguageChecks.csproj -- .
```

Use `-p:Platform=ARM64` for an ARM64 build. Ordinary builds, including Release configuration, use the **Fallery Dev** identity.

To create an installer and update packages:

```powershell
./tools/Build-Release.ps1 -Version 0.1.0 -Architecture x64
```

The GitHub Actions **Build release** workflow builds both architectures and creates a release draft for review. See [DEPLOYMENT.md](DEPLOYMENT.md) for the complete process and [Tests/README.md](Tests/README.md) for validation commands.

## Local data

| Build | Data directory |
| --- | --- |
| Installed stable release | `%LOCALAPPDATA%\Fallery` |
| Preview release | `%LOCALAPPDATA%\Fallery.Preview` |
| Local development build | `%LOCALAPPDATA%\Fallery.Dev` |

These directories contain settings, playback positions, the local search index, and thumbnail caches. Originals remain in the folders you browse; explicit file-management commands act on the files you select. Stable, preview, and development builds use separate data directories. The full directory inventory and backup behavior are documented in [DEPLOYMENT.md](DEPLOYMENT.md).

## Credits

- Interface: Windows App SDK / WinUI 3 and the .NET Community Toolkit.
- Icons: Hugeicons Stroke Rounded; see the [bundled font license](Assets/Fonts/LICENSE-Hugeicons.txt).
- Installation and updates: [Velopack](https://github.com/velopack/velopack).

Fallery is licensed under the MIT License. Third-party components retain their respective licenses.
