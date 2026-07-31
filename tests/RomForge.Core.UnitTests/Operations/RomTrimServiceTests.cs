using System;
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

[TestOf(typeof(RomTrimService))]
public sealed class RomTrimServiceTests
{
    private const long RomSize = 4 * 1024 * 1024;

    private Mock<IArchiveExtractor> _extractor = null!;
    private Mock<IArchiveCompressor> _compressor = null!;
    private Mock<IRomFileOperations> _fileOps = null!;
    private RomTrimService _service = null!;

    [SetUp]
    public void SetUp()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        AppDataService appData = new AppDataService(root);
        ILogger logger = new LoggerConfiguration().CreateLogger();

        _extractor = new Mock<IArchiveExtractor>();
        _compressor = new Mock<IArchiveCompressor>();
        _fileOps = new Mock<IRomFileOperations>();
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());
        _fileOps.Setup(f => f.DeleteAsync(It.IsAny<string>())).ReturnsAsync(Result.Ok());
        _fileOps
            .Setup(f => f.TruncateAsync(It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(Result.Ok());

        ArchiveWorkspace workspace = new ArchiveWorkspace(appData, _fileOps.Object, logger);
        _service = new RomTrimService(
            _extractor.Object,
            _compressor.Object,
            _fileOps.Object,
            workspace
        );
    }

    private static MatchResult Untrimmed(string filePath = "/roms/Old.7z") =>
        new MatchResult
        {
            Game = new Game
            {
                ReleaseNumber = 1,
                Title = "Test Game",
                RomSize = RomSize,
                Files = new GameFiles { RomCrc = 0x12345678, RomExtension = "gba" },
            },
            Status = MatchStatus.Verified,
            ScannedRom = new ScannedRom
            {
                FilePath = filePath,
                FileExtension = "7z",
                RomExtension = "gba",
                TrimmedCrc = 0x99999999,
            },
            IsUntrimmed = true,
        };

    private void SetupExtract(Result<string> result) =>
        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void SetupCompress(Result result) =>
        _compressor
            .Setup(c =>
                c.CompressAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(result);

    [Test]
    public async Task TrimAsync_ExtractFails_ReturnsFailureAndDoesNotTruncate()
    {
        SetupExtract(Result.Fail("extract failed"));

        Result<MatchResult> result = await _service.TrimAsync(
            Untrimmed(),
            ("/roms/Old.7z", "/roms/0001 - Test Game.7z"),
            "7z",
            CancellationToken.None
        );

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().StartWith("Old.7z:");
        result.Errors[0].Message.Should().Contain("extract failed");
        _fileOps.Verify(f => f.TruncateAsync(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    [Test]
    public async Task TrimAsync_TruncateFails_ReturnsFailureAndDoesNotCompress()
    {
        SetupExtract(Result.Ok("/tmp/extracted.rom"));
        _fileOps
            .Setup(f => f.TruncateAsync(It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(Result.Fail("truncate failed"));

        Result<MatchResult> result = await _service.TrimAsync(
            Untrimmed(),
            ("/roms/Old.7z", "/roms/0001 - Test Game.7z"),
            "7z",
            CancellationToken.None
        );

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("truncate failed");
    }

    [Test]
    public async Task TrimAsync_CompressFails_ReturnsFailure()
    {
        SetupExtract(Result.Ok("/tmp/extracted.rom"));
        SetupCompress(Result.Fail("compress failed"));

        Result<MatchResult> result = await _service.TrimAsync(
            Untrimmed(),
            ("/roms/Old.7z", "/roms/0001 - Test Game.7z"),
            "7z",
            CancellationToken.None
        );

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("compress failed");
    }

    [Test]
    public async Task TrimAsync_Succeeds_ReturnsTrimmedVerifiedMatch()
    {
        SetupExtract(Result.Ok("/tmp/extracted.rom"));
        SetupCompress(Result.Ok());

        Result<MatchResult> result = await _service.TrimAsync(
            Untrimmed(),
            ("/roms/Old.7z", "/roms/0001 - Test Game.7z"),
            "7z",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsUntrimmed.Should().BeFalse();
        result.Value.Status.Should().Be(MatchStatus.Verified);
        result.Value.ScannedRom!.FilePath.Should().Be("/roms/0001 - Test Game.7z");
        result.Value.ScannedRom.FileExtension.Should().Be("7z");
        result.Value.ScannedRom.Crc.Should().Be(0x12345678);
        result.Value.ScannedRom.TrimmedCrc.Should().BeNull();
    }
}
