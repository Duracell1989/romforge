using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Scanning;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.UnitTests.Services;

[TestOf(typeof(ScanResultStore))]
public sealed class ScanResultStoreTests
{
    private string _tempDir = string.Empty;
    private AppDataService _appData = null!;
    private ScanResultStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _appData = new AppDataService(_tempDir);
        _store = new ScanResultStore(_appData, new LoggerConfiguration().CreateLogger());
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    [Test]
    public async Task InitializeAsync_Succeeds_WithoutError()
    {
        await _store.Invoking(s => s.InitializeAsync()).Should().NotThrowAsync();
    }

    [Test]
    public async Task LoadResultsAsync_BeforeInit_ReturnsEmpty()
    {
        DatFile dat = MakeDat("TestDat", [MakeGame(1)]);

        IReadOnlyList<MatchResult> results = await _store.LoadResultsAsync("TestDat", dat);

        results.Should().BeEmpty();
    }

    [Test]
    public async Task LoadResultsAsync_NoSavedData_ReturnsEmpty()
    {
        await _store.InitializeAsync();
        DatFile dat = MakeDat("TestDat", [MakeGame(1)]);

        IReadOnlyList<MatchResult> results = await _store.LoadResultsAsync("TestDat", dat);

        results.Should().BeEmpty();
    }

    [Test]
    public async Task SaveAndLoad_RoundTripsVerifiedResult()
    {
        await _store.InitializeAsync();
        Game game = MakeGame(1);
        DatFile dat = MakeDat("TestDat", [game]);
        ScannedRom rom = new ScannedRom
        {
            FilePath = "/roms/game.gba",
            FileExtension = "gba",
            RomExtension = "gba",
            Crc = 0xAABBCCDD,
        };
        List<MatchResult> toSave =
        [
            new MatchResult { Game = game, Status = MatchStatus.Verified, ScannedRom = rom },
        ];

        await _store.SaveResultsAsync("TestDat", toSave);
        IReadOnlyList<MatchResult> loaded = await _store.LoadResultsAsync("TestDat", dat);

        loaded.Should().HaveCount(1);
        loaded[0].Status.Should().Be(MatchStatus.Verified);
        loaded[0].ScannedRom.Should().NotBeNull();
        loaded[0].ScannedRom!.FilePath.Should().Be("/roms/game.gba");
        loaded[0].ScannedRom!.Crc.Should().Be(0xAABBCCDD);
    }

    [Test]
    public async Task SaveAndLoad_RoundTripsMissingResult()
    {
        await _store.InitializeAsync();
        Game game = MakeGame(5);
        DatFile dat = MakeDat("TestDat", [game]);
        List<MatchResult> toSave =
        [
            new MatchResult { Game = game, Status = MatchStatus.Missing, ScannedRom = null },
        ];

        await _store.SaveResultsAsync("TestDat", toSave);
        IReadOnlyList<MatchResult> loaded = await _store.LoadResultsAsync("TestDat", dat);

        loaded.Should().HaveCount(1);
        loaded[0].Status.Should().Be(MatchStatus.Missing);
        loaded[0].ScannedRom.Should().BeNull();
    }

    [Test]
    public async Task LoadResultsAsync_GameNotInDb_ReturnsMissingForThatGame()
    {
        await _store.InitializeAsync();
        Game game1 = MakeGame(1);
        Game game2 = MakeGame(2);
        Game game3 = MakeGame(3);
        DatFile dat = MakeDat("TestDat", [game1, game2, game3]);

        // Save only game1
        await _store.SaveResultsAsync(
            "TestDat",
            [new MatchResult { Game = game1, Status = MatchStatus.Verified, ScannedRom = null }]
        );

        IReadOnlyList<MatchResult> loaded = await _store.LoadResultsAsync("TestDat", dat);

        loaded.Should().HaveCount(3);
        loaded[0].Status.Should().Be(MatchStatus.Verified);
        loaded[1].Status.Should().Be(MatchStatus.Missing);
        loaded[2].Status.Should().Be(MatchStatus.Missing);
    }

    [Test]
    public async Task SaveResultsAsync_CalledTwice_SecondCallReplaces()
    {
        await _store.InitializeAsync();
        Game game = MakeGame(1);
        DatFile dat = MakeDat("TestDat", [game]);

        await _store.SaveResultsAsync(
            "TestDat",
            [new MatchResult { Game = game, Status = MatchStatus.Missing, ScannedRom = null }]
        );
        await _store.SaveResultsAsync(
            "TestDat",
            [new MatchResult { Game = game, Status = MatchStatus.Verified, ScannedRom = null }]
        );

        IReadOnlyList<MatchResult> loaded = await _store.LoadResultsAsync("TestDat", dat);

        loaded.Should().HaveCount(1);
        loaded[0].Status.Should().Be(MatchStatus.Verified);
    }

    [Test]
    public async Task UpdateResultAsync_ChangesStatusOfSingleRow()
    {
        await _store.InitializeAsync();
        Game game1 = MakeGame(1);
        Game game2 = MakeGame(2);
        DatFile dat = MakeDat("TestDat", [game1, game2]);

        await _store.SaveResultsAsync(
            "TestDat",
            [
                new MatchResult { Game = game1, Status = MatchStatus.Missing, ScannedRom = null },
                new MatchResult { Game = game2, Status = MatchStatus.Missing, ScannedRom = null },
            ]
        );

        await _store.UpdateResultAsync(
            "TestDat",
            new MatchResult { Game = game1, Status = MatchStatus.Verified, ScannedRom = null }
        );

        IReadOnlyList<MatchResult> loaded = await _store.LoadResultsAsync("TestDat", dat);

        loaded[0].Status.Should().Be(MatchStatus.Verified);
        loaded[1].Status.Should().Be(MatchStatus.Missing);
    }

    [Test]
    public async Task SaveResultsAsync_MultipleDataNames_IsolatedPerDat()
    {
        await _store.InitializeAsync();
        Game game = MakeGame(1);
        DatFile datA = MakeDat("DatA", [game]);
        DatFile datB = MakeDat("DatB", [game]);

        await _store.SaveResultsAsync(
            "DatA",
            [new MatchResult { Game = game, Status = MatchStatus.Verified, ScannedRom = null }]
        );
        await _store.SaveResultsAsync(
            "DatB",
            [new MatchResult { Game = game, Status = MatchStatus.Missing, ScannedRom = null }]
        );

        IReadOnlyList<MatchResult> loadedA = await _store.LoadResultsAsync("DatA", datA);
        IReadOnlyList<MatchResult> loadedB = await _store.LoadResultsAsync("DatB", datB);

        loadedA[0].Status.Should().Be(MatchStatus.Verified);
        loadedB[0].Status.Should().Be(MatchStatus.Missing);
    }

    [Test]
    public async Task SaveAndLoad_RoundTripsAllFlags()
    {
        await _store.InitializeAsync();
        Game game = MakeGame(1);
        DatFile dat = MakeDat("TestDat", [game]);
        ScannedRom rom = new ScannedRom
        {
            FilePath = "/roms/game.zip",
            FileExtension = "zip",
            RomExtension = "gba",
            Crc = 0x11223344,
        };
        List<MatchResult> toSave =
        [
            new MatchResult
            {
                Game = game,
                Status = MatchStatus.Verified,
                ScannedRom = rom,
                IsIncorrectlyNamed = true,
                IsWrongArchiveType = true,
                IsUntrimmed = false,
                IsReArchived = true,
            },
        ];

        await _store.SaveResultsAsync("TestDat", toSave);
        IReadOnlyList<MatchResult> loaded = await _store.LoadResultsAsync("TestDat", dat);

        loaded.Should().HaveCount(1);
        loaded[0].IsIncorrectlyNamed.Should().BeTrue();
        loaded[0].IsWrongArchiveType.Should().BeTrue();
        loaded[0].IsUntrimmed.Should().BeFalse();
        loaded[0].IsReArchived.Should().BeTrue();
    }

    [Test]
    public async Task UpdateResultAsync_UpdatesFlags()
    {
        await _store.InitializeAsync();
        Game game = MakeGame(1);
        DatFile dat = MakeDat("TestDat", [game]);

        await _store.SaveResultsAsync(
            "TestDat",
            [new MatchResult { Game = game, Status = MatchStatus.Verified, IsWrongArchiveType = true }]
        );

        await _store.UpdateResultAsync(
            "TestDat",
            new MatchResult { Game = game, Status = MatchStatus.Verified, IsReArchived = true }
        );

        IReadOnlyList<MatchResult> loaded = await _store.LoadResultsAsync("TestDat", dat);

        loaded[0].IsWrongArchiveType.Should().BeFalse();
        loaded[0].IsReArchived.Should().BeTrue();
    }

    [Test]
    public async Task SaveAndLoad_LastModified_RoundTripsAsUtc()
    {
        await _store.InitializeAsync();
        Game game = MakeGame(1);
        DatFile dat = MakeDat("TestDat", [game]);

        // EU DST "fall back" moment: 2024-10-27 01:00 UTC = 02:00 local (CEST) = 01:00 local (CET).
        // If the store accidentally converts to or from local time, the ticks will differ by ±3600s
        // and DateTimeKind will not be Utc.
        DateTime dstBoundary = new DateTime(2024, 10, 27, 1, 0, 0, DateTimeKind.Utc);
        ScannedRom rom = new ScannedRom
        {
            FilePath = "/roms/game.gba",
            FileExtension = "gba",
            RomExtension = "gba",
            Crc = 0xDEADBEEF,
            LastModified = dstBoundary,
        };
        List<MatchResult> toSave =
        [
            new MatchResult { Game = game, Status = MatchStatus.Verified, ScannedRom = rom },
        ];

        await _store.SaveResultsAsync("TestDat", toSave);
        IReadOnlyList<MatchResult> loaded = await _store.LoadResultsAsync("TestDat", dat);

        DateTime? loaded_time = loaded[0].ScannedRom!.LastModified;
        loaded_time.Should().NotBeNull();
        loaded_time!.Value.Kind.Should().Be(DateTimeKind.Utc);
        loaded_time.Value.Should().Be(dstBoundary);
    }

    private static Game MakeGame(int releaseNumber) =>
        new Game
        {
            ReleaseNumber = releaseNumber,
            Title = $"Game {releaseNumber}",
            Files = new GameFiles { RomCrc = (uint)(0x1000 + releaseNumber), RomExtension = "gba" },
        };

    private static DatFile MakeDat(string datName, IReadOnlyList<Game> games) =>
        new DatFile
        {
            Header = new DatHeader { DatName = datName },
            Games = [.. games],
        };
}
