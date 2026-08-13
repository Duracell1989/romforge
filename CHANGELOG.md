# Changelog

All notable changes to RomForge are documented here. This project follows [Semantic Versioning](https://semver.org).

## [Unreleased]

## [1.5.5] — 2026-08-13

### Changed

- No user-facing changes. Build-time static analysis (Roslynator and .NET IDE style rules) is now enforced, catching issues that were previously invisible locally until CI ran a separate analysis pass.

## [1.5.4] — 2026-08-12

### Fixed

- Re-archiving large ROMs no longer exhausts system memory and stalls the machine. 7-Zip was splitting large inputs into multiple blocks and encoding them in parallel, each encoder holding its own dictionary-sized allocation, so peak memory tracked the size of the ROM rather than the dictionary — a 4 GB 3DS cart could reach roughly 42 GB in a single job. Compression now uses a single encoder with a capped dictionary, holding memory flat at about 2.6 GB per job at any ROM size, with no measurable increase in archive size.
- "Re-Archive All" no longer re-packs the entire library on every run. DATs that store the ROM extension with a leading dot produced archive entries with a doubled dot ("Name..3ds"), which never matched on re-scan, so those ROMs stayed re-archive targets forever. Archives written by earlier versions are genuinely misnamed and will each be re-archived once, after which they settle as "Good".
- Memory throttling now applies to every compression path. Previously only bulk re-archive was throttled, so a single re-archive, a single trim and a bulk trim each ran unthrottled and could claim a full memory budget at the same time.
- Dictionary size and concurrency now adapt to the amount of memory available, so a single job can no longer exceed the entire memory budget on machines with less RAM.
- "Trim" is now correctly disabled while a bulk re-archive is running, matching the other operations.

## [1.5.3] — 2026-08-08

### Fixed

- Cancelling a bulk re-archive no longer crashes in-flight files with a `NullReferenceException`
- "Rename All" no longer runs its work before the progress window appears, then flashes the window closed instantly
- Re-archiving large ROMs (e.g. 3DS) concurrently is now also throttled by estimated memory usage, not just CPU core count, to prevent exhausting system memory

## [1.5.2] — 2026-07-31

### Fixed

- "Rename All" no longer reports success while changing nothing for ROMs whose only problem was a misnamed internal archive entry; the outer-filename rename and the internal entry name are now tracked separately, so these ROMs are correctly routed to Re-Archive instead. A re-scan is required to reclassify ROMs stored under the previous version.

## [1.5.1] — 2026-07-30

### Fixed

- ROMs re-archived before the archive entry-naming fix could still show as "Good" despite having a garbage internal archive entry name; RomForge now detects the mismatch and requires a re-archive to clear it

## [1.5.0] — 2026-07-29

### Changed

- ROMs are now always renamed as "release - title", regardless of what naming template a DAT's own config specifies

### Fixed

- Fixed a bug where re-archiving or trimming a ROM could give its internal archive entry a corrupted name (a leftover random extension) when the ROM's real extension is empty

## [1.4.1] — 2026-07-29

### Fixed

- The "Updating DAT" and "Downloading Images" dialogs now show which DAT they're working on, instead of a generic title

## [1.4.0] — 2026-07-29

### Changed

- Replaced the SharpCompress + external `7zz` CLI archiving engine with SevenZipSharper, a bundled native library — RomForge no longer needs Homebrew's `sevenzip` package installed for compression to work
- Re-archiving now tunes the 7z dictionary size to each ROM, improving compression ratios

### Fixed

- Fixed a data-loss bug where re-archiving a ROM to its own filename could destroy it if the app was interrupted mid-operation
- Patched a high-severity vulnerability in a bundled SQLite component (GHSA-2m69-gcr7-jv3q)

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
