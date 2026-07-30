using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using Moq;
using NUnit.Framework;
using RomForge.Core.IO;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Operations;
using RomForge.Core.Scanning;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.UnitTests.Operations;

[TestOf(typeof(DatLibraryService))]
public sealed class DatLibraryServiceTests
{
    private const string DatName = "Test DAT";

    private Mock<IDatReader> _datReader = null!;
    private Mock<IDatImporter> _datImporter = null!;
    private Mock<IRomFileOperations> _fileOps = null!;
    private ScanResultStore _scanResultStore = null!;
    private DatLibraryService _service = null!;

    [SetUp]
    public async Task SetUp()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        AppDataService appData = new AppDataService(root);
        ILogger logger = new LoggerConfiguration().CreateLogger();

        _datReader = new Mock<IDatReader>();
        _datImporter = new Mock<IDatImporter>();
        _fileOps = new Mock<IRomFileOperations>();
        _scanResultStore = new ScanResultStore(appData, logger);
        await _scanResultStore.InitializeAsync();

        _service = new DatLibraryService(
            _ => _datReader.Object,
            _datImporter.Object,
            new DatConfigService(appData, logger),
            _scanResultStore,
            _fileOps.Object,
            appData,
            logger
        );
    }

    private static Game MakeGame(int releaseNumber) =>
        new Game
        {
            ReleaseNumber = releaseNumber,
            Title = $"Game {releaseNumber}",
            RomSize = 1024,
            Files = new GameFiles { RomCrc = 0x12345678, RomExtension = "gba" },
        };

    private static DatFile MakeDatFile(params Game[] games) =>
        new DatFile
        {
            Header = new DatHeader { DatName = DatName },
            Games = games,
        };

    [Test]
    public async Task ReadAsync_DelegatesToReaderFactory()
    {
        DatFile datFile = MakeDatFile(MakeGame(1));
        _datReader
            .Setup(r => r.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(datFile));

        Result<DatFile> result = await _service.ReadAsync("/some/path.dat");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(datFile);
    }

    [Test]
    public async Task ImportAsync_ImportFails_ReturnsFailure()
    {
        _datImporter
            .Setup(i =>
                i.ImportAsync(
                    It.IsAny<string>(),
                    It.IsAny<DatHeader>(),
                    It.IsAny<IProgress<ImportProgress>?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Fail("import boom"));

        Result<string> result = await _service.ImportAsync(
            "/src/list.dat",
            new DatHeader { DatName = DatName },
            progress: null
        );

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("import boom");
    }

    [Test]
    public async Task ImportAsync_ImportSucceeds_ReturnsManagedPath()
    {
        _datImporter
            .Setup(i =>
                i.ImportAsync(
                    It.IsAny<string>(),
                    It.IsAny<DatHeader>(),
                    It.IsAny<IProgress<ImportProgress>?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Ok("/managed/list.dat"));

        Result<string> result = await _service.ImportAsync(
            "/src/list.dat",
            new DatHeader { DatName = DatName },
            progress: null
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("/managed/list.dat");
    }

    [Test]
    public async Task LoadResultsAsync_NoPersisted_ReturnsFreshMatchNotFromCache()
    {
        DatFile datFile = MakeDatFile(MakeGame(1), MakeGame(2));

        (IReadOnlyList<MatchResult> results, bool fromCache) = await _service.LoadResultsAsync(
            datFile
        );

        fromCache.Should().BeFalse();
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Status == MatchStatus.Missing);
    }

    [Test]
    public async Task LoadResultsAsync_Persisted_ReturnsFromCache()
    {
        DatFile datFile = MakeDatFile(MakeGame(1));
        await _scanResultStore.SaveResultsAsync(
            DatName,
            new List<MatchResult>
            {
                new MatchResult { Game = MakeGame(1), Status = MatchStatus.Verified },
            }
        );

        (IReadOnlyList<MatchResult> results, bool fromCache) = await _service.LoadResultsAsync(
            datFile
        );

        fromCache.Should().BeTrue();
        results.Should().HaveCount(1);
    }

    [Test]
    public async Task FindAndClearStaleAsync_FolderUnavailable_ReturnsEmpty()
    {
        _fileOps.Setup(f => f.DirectoryExists("/roms")).Returns(false);
        List<MatchResult> results = new List<MatchResult>
        {
            new MatchResult
            {
                Game = MakeGame(1),
                Status = MatchStatus.Verified,
                ScannedRom = new ScannedRom { FilePath = "/roms/gone.7z" },
            },
        };

        IReadOnlyList<MatchResult> cleared = await _service.FindAndClearStaleAsync(
            DatName,
            "/roms",
            results
        );

        cleared.Should().BeEmpty();
    }

    [Test]
    public async Task FindAndClearStaleAsync_NoStale_ReturnsEmpty()
    {
        List<MatchResult> results = new List<MatchResult>
        {
            new MatchResult { Game = MakeGame(1), Status = MatchStatus.Missing },
        };

        IReadOnlyList<MatchResult> cleared = await _service.FindAndClearStaleAsync(
            DatName,
            romFolder: null,
            results
        );

        cleared.Should().BeEmpty();
    }

    [Test]
    public async Task FindAndClearStaleAsync_StaleFile_PersistsMissingAndReturnsCleared()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            "gone.7z"
        );
        List<MatchResult> results = new List<MatchResult>
        {
            new MatchResult
            {
                Game = MakeGame(1),
                Status = MatchStatus.Verified,
                ScannedRom = new ScannedRom { FilePath = missingPath },
            },
        };

        IReadOnlyList<MatchResult> cleared = await _service.FindAndClearStaleAsync(
            DatName,
            romFolder: null,
            results
        );

        cleared.Should().HaveCount(1);
        cleared[0].Status.Should().Be(MatchStatus.Missing);
        cleared[0].Game.ReleaseNumber.Should().Be(1);

        // The missing status was persisted.
        DatFile datFile = MakeDatFile(MakeGame(1));
        IReadOnlyList<MatchResult> persisted = await _scanResultStore.LoadResultsAsync(
            DatName,
            datFile
        );
        persisted.Should().ContainSingle(r => r.Status == MatchStatus.Missing);
    }
}
