# Changelog

All notable changes to RomForge are documented here. This project follows [Semantic Versioning](https://semver.org).

## [Unreleased]

## [1.3.0] — 2026-07-19

### Added

- The status bar now shows the running app version, with a link to the GitHub releases page; an "About RomForge…" menu item shows the same info

### Fixed

- Scan results are no longer wiped to "Missing" when a DAT's ROM folder is on an offline or not-yet-mounted external drive at startup
- Re-archiving or trimming a ROM no longer risks losing it if the destination becomes unreachable mid-operation (e.g. a drive unmounts) — the compressed copy is recovered to a dedicated folder instead of being silently discarded
- Deleting an entire ROM subfolder while its drive stays online is now correctly detected and reported, instead of being silently ignored

## [1.2.0] — 2026-07-15

### Added

- In-app update check: RomForge now checks GitHub for a newer release at startup and lets you know when an update is available
- A DAT menu command to download any missing box-art on demand, with a live "X of Y" progress log you can cancel

### Changed

- Updated the Avalonia UI toolkit to 12.1.0 and set the application name shown in the macOS menu bar

### Fixed

- Cancelling a re-archive no longer crashes the app
- Re-archiving now replaces a stale destination archive instead of appending to it, closing a path that could silently corrupt a ROM
- Preferences writes are serialized, so two settings changes in quick succession can no longer overwrite each other and lose a setting
- Hardened the re-archive status database against concurrent-write races that could drop a persisted re-archive mark

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
