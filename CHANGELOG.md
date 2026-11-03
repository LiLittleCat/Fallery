# Changelog

All notable changes to Fallery are documented here, following
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.2] - 2026-11-03

### Fixed

- Check stable GitHub updates through the public release feed instead of the rate-limited API, and pin package downloads to the checked version's release tag.

## [0.1.1] - 2026-11-03

### Changed

- Documented v0.1.0 release verification, public downloads, and remaining installation and upgrade checks.
- Centered the title, introduction, and language links in both READMEs and removed the former Chinese project name.
- Kept changelog maintenance in the development rules and automatic version-specific release notes in the build and publication workflows.
- Build packages on GitHub Actions by default, with optional release-draft creation and manual publication.

### Removed

- WebP metadata reading checks that depend on optional Windows property handlers.

### Fixed

- Include the application's WinUI resource index and compiled XAML in published packages, and reject missing resources before packaging to prevent startup crashes after installation.

## [0.1.0] - 2026-11-02

### Added

- Masonry, justified, grid, filmstrip, and list views with virtualized browsing and cached thumbnails.
- Folder previews, favorites, navigation history, keyboard shortcuts, gestures, and independent windows.
- Image and GIF viewing, zoom and pan, full screen, photo metadata, and motion photo playback.
- Video and audio playback with playlists, playback speed, resume positions, and A–B repeat, using Windows codecs.
- Search, sorting, media filters, folder exclusions, file operations, and duplicate image detection.
- Light, dark, and system themes; live English and Simplified Chinese language switching.
- x64 and ARM64 installers, portable builds, and in-app update checks, downloads, and restart confirmation.
- Separate stable, preview, and development data directories, with settings and playback backups before in-app updates.
- English and Chinese documentation, sample screenshots, and an MIT license credited to Yi Liu.
- GitHub Actions release builds, update feeds, SHA-256 checksums, and version-specific release notes.

### Fixed

- Core checks now verify basic information fallback on Windows systems without a WebP property handler.

[Unreleased]: https://github.com/LiLittleCat/Fallery/compare/v0.1.2...HEAD
[0.1.2]: https://github.com/LiLittleCat/Fallery/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/LiLittleCat/Fallery/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/LiLittleCat/Fallery/releases/tag/v0.1.0
