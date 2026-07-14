# Changelog

All notable changes to RomForge are documented here. This project follows [Semantic Versioning](https://semver.org).

## [Unreleased]

## [1.1.0] — 2026-07-14

### Added

- Download missing box-art: after a DAT update, RomForge fetches only the images you don't already have and shows a live log with an "X of Y" counter that you can cancel

### Fixed

- A ROM is now only counted "Good" once RomForge has re-archived it — a freshly scanned, coincidentally-correct file no longer shows as good before it has been rewritten

## [1.0.0] — 2026-07-13

First stable release.

### Added

- Settings screen (File → Settings…, ⌘,): global default archive format and a default destination folder for unverified ROMs
- Native macOS menu bar and a right-click context menu on the game list, plus a streamlined toolbar and status filter chips
- Signed and notarized macOS build — the app now launches without a Gatekeeper warning

### Changed

- Reworked per-game match status into composable flags, so multiple issues on one ROM are tracked and shown together

### Security

- Fixed a zip-slip path-traversal weakness when extracting archives

## [0.1.0] — 2026-06-23

Initial release.

### Added

- Import and manage OfflineList-format DAT files (ZIP-wrapped or raw XML)
- Scan ROM folders and match against DAT entries by CRC32
- Visual match status per game: Verified, Missing, Incorrectly Named, Wrong Archive Type, Untrimmed
- Sortable columns (release number, title, publisher, status) and per-status filter checkboxes
- Rename ROMs to DAT-expected filenames
- Re-archive ROMs between ZIP and 7z formats
- Trim ROMs (GBA/NDS padding removal)
- Auto-update DAT files from their configured update URL
- Scan cache keyed by folder path — only re-hashes files that have changed
- Status persistence via SQLite — game list survives restarts without re-scanning
- Multi-DAT support — open and switch between multiple DAT files in one session
- Progress dialog with cancellation for all long-running operations
- macOS, Windows, and Linux support via Avalonia UI
