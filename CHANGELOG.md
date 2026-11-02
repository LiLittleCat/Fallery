# Changelog

All notable changes to Fallery are documented here, following
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.1] - 2026-11-02

### Changed

- Documented v0.1.0 release verification, public downloads, and remaining installation and upgrade checks.
- Centered the title, introduction, and language links in both READMEs and removed the former Chinese project name.
- Kept changelog maintenance in the development rules; release notes can be supplied explicitly or linked from the release page.
- Build release packages on GitHub Actions and leave the release as a draft for manual publication.

### Removed

- Automatic changelog extraction and validation from build scripts and CI.

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

[Unreleased]: https://github.com/LiLittleCat/Fallery/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/LiLittleCat/Fallery/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/LiLittleCat/Fallery/releases/tag/v0.1.0
