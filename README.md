# RomForge

A cross-platform ROM management tool for the OfflineList DAT ecosystem.

[![CI](https://github.com/Duracell1989/RomForge/actions/workflows/ci.yml/badge.svg)](https://github.com/Duracell1989/RomForge/actions/workflows/ci.yml)

RomForge is a modern rewrite of OfflineList — a Windows-only C++ tool that has had no active development since the mid-2000s. It preserves full compatibility with the existing community DAT files while bringing a native cross-platform UI built on .NET 10 and [Avalonia](https://avaloniaui.net).

---

## Features

- Import and manage OfflineList-format DAT files
- Scan ROM folders and match against DAT entries by CRC32
- Visual status per game: Verified, Missing, Incorrectly Named, Wrong Archive Type, Untrimmed
- Sortable and filterable game list
- Rename ROMs to DAT-expected filenames
- Re-archive ROMs between ZIP and 7z formats
- Trim ROMs (GBA/NDS padding removal)
- Auto-update DAT files from their configured update URL
- Scan cache — only re-hashes files that have changed
- Status persistence — game list survives restarts without re-scanning
- Multi-DAT support

---

## Platform support

| Platform | Architecture | Status |
|---|---|---|
| macOS | Apple Silicon (arm64) | ✅ Primary |
| macOS | Intel (x64) | ✅ Supported |
| Windows | x64 | ✅ Supported |
| Linux | x64 | ✅ Supported |

---

## Installation

Download the latest release for your platform from the [Releases](../../releases) page.

**macOS:** Download `RomForge-macOS-arm64.dmg` (Apple Silicon). Open the `.dmg` and drag `RomForge.app` to your Applications folder. The app is signed and notarized, so it launches normally — no Gatekeeper workaround needed.

**Windows:** Download `RomForge-Windows-x64.zip`. Extract and run `RomForge.exe`.

**Linux:** Download `RomForge-Linux-x64.tar.gz`. Extract and run `./RomForge`.

---

## Building from source

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
git clone git@github.com:Duracell1989/RomForge.git
cd RomForge
make build    # dotnet build
make test     # run all tests
make run      # launch the UI
```

macOS self-contained bundle:

```bash
make package  # produces artifacts/RomForge.app
```

---

## DAT file compatibility

RomForge reads DAT files in the **OfflineList XML format**. You supply the DATs yourself: they contain only checksums and filenames — no ROM data — so they are freely distributable metadata.

The maintained source for these DATs is the [No-Intro](https://datomatic.no-intro.org) preservation project's DAT-o-Matic. When downloading, pick the **OfflineList** export format — RomForge does not currently read the No-Intro/Logiqx format. DATs can be imported directly from their ZIP archives; no manual extraction is required.

---

## License

MIT — see [LICENSE](LICENSE).
