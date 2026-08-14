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

namespace RomForge.Core.UnitTests.Operations
{
    [TestOf(typeof(RomReArchiveService))]
    public sealed class RomReArchiveServiceTests
    {
        private const string DatName = "Test DAT";

        private Mock<IArchiveExtractor> _extractor = null!;
        private Mock<IArchiveCompressor> _compressor = null!;
        private Mock<IRomFileOperations> _fileOps = null!;
        private ReArchiveStore _reArchiveStore = null!;
        private ScanResultStore _scanStore = null!;
        private RomReArchiveService _service = null!;

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

            ArchiveWorkspace workspace = new ArchiveWorkspace(appData, _fileOps.Object, logger);
            _reArchiveStore = new ReArchiveStore(appData, logger);
            _scanStore = new ScanResultStore(appData, logger);
            _service = new RomReArchiveService(
                _extractor.Object,
                _compressor.Object,
                _fileOps.Object,
                workspace,
                _reArchiveStore,
                _scanStore,
                new WorkingSetBudgetGate(1_000_000_000_000L)
            );
        }

        private static readonly byte[] ValidRomBytes = [0x01, 0x02, 0x03, 0x04];
        private static readonly uint ValidRomCrc = RomScanner.ComputeCrcs(ValidRomBytes).FullCrc;

        private static MatchResult Match(string filePath, uint? crc = null) =>
            new MatchResult
            {
                Game = new Game
                {
                    ReleaseNumber = 1,
                    Title = "Test Game",
                    Files = new GameFiles { RomCrc = crc ?? ValidRomCrc, RomExtension = "gba" },
                },
                Status = MatchStatus.Verified,
                ScannedRom = new ScannedRom
                {
                    FilePath = filePath,
                    FileExtension = "zip",
                    RomExtension = "gba",
                    Crc = crc ?? ValidRomCrc,
                },
                IsWrongArchiveType = true,
            };

        private void SetupExtract(Result<string> result) =>
            _extractor
                .Setup(e =>
                    e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                )
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
        public async Task ReArchiveAsync_ExtractFails_ReturnsFailureAndDoesNotCompress()
        {
            SetupExtract(Result.Fail("extract failed"));

            Result<MatchResult> result = await _service.ReArchiveAsync(
                Match("/roms/Old.zip"),
                ("/roms/Old.zip", "/roms/0001 - Test Game.7z"),
                "7z",
                DatName,
                CancellationToken.None
            );

            result.IsFailed.Should().BeTrue();
            result.Errors[0].Message.Should().Contain("extract failed");
            _compressor.Verify(
                c =>
                    c.CompressAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<long>(),
                        It.IsAny<IProgress<int>?>(),
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()
                    ),
                Times.Never
            );
        }

        [Test]
        public async Task ReArchiveAsync_CompressFails_ReturnsFailureWithFileNamePrefix()
        {
            SetupExtract(Result.Ok("/tmp/extracted.rom"));
            SetupCompress(Result.Fail("compress failed"));

            Result<MatchResult> result = await _service.ReArchiveAsync(
                Match("/roms/Old.zip"),
                ("/roms/Old.zip", "/roms/0001 - Test Game.7z"),
                "7z",
                DatName,
                CancellationToken.None
            );

            result.IsFailed.Should().BeTrue();
            result.Errors[0].Message.Should().StartWith("Old.zip:");
            result.Errors[0].Message.Should().Contain("compress failed");
        }

        [Test]
        public async Task ReArchiveAsync_Succeeds_ReturnsGoodReArchivedMatch()
        {
            await _reArchiveStore.InitializeAsync();
            await _scanStore.InitializeAsync();
            SetupExtract(Result.Ok("/tmp/extracted.rom"));
            SetupCompress(Result.Ok());
            _fileOps
                .Setup(f => f.OpenReadAsync(It.IsAny<string>()))
                .ReturnsAsync(() => new MemoryStream(ValidRomBytes));

            Result<MatchResult> result = await _service.ReArchiveAsync(
                Match("/roms/Old.zip"),
                ("/roms/Old.zip", "/roms/0001 - Test Game.7z"),
                "7z",
                DatName,
                CancellationToken.None
            );

            result.IsSuccess.Should().BeTrue();
            result.Value.IsReArchived.Should().BeTrue();
            result.Value.IsEntryMisnamed.Should().BeFalse();
            result.Value.IsWrongArchiveType.Should().BeFalse();
            result.Value.IsGood.Should().BeTrue();
            result.Value.ScannedRom!.FilePath.Should().Be("/roms/0001 - Test Game.7z");
            result.Value.ScannedRom.FileExtension.Should().Be("7z");
        }

        [Test]
        public async Task ReArchiveAsync_PostWriteCrcMismatch_ReturnsFailureAndDoesNotMarkReArchived()
        {
            await _reArchiveStore.InitializeAsync();
            await _scanStore.InitializeAsync();
            SetupExtract(Result.Ok("/tmp/extracted.rom"));
            SetupCompress(Result.Ok());
            // Bytes deliberately different from ValidRomBytes, so the placed archive's CRC
            // never matches the DAT's expected CRC — simulating a corrupted write.
            _fileOps
                .Setup(f => f.OpenReadAsync(It.IsAny<string>()))
                .ReturnsAsync(() => new MemoryStream([0xDE, 0xAD, 0xBE, 0xEF]));

            Result<MatchResult> result = await _service.ReArchiveAsync(
                Match("/roms/Old.zip"),
                ("/roms/Old.zip", "/roms/0001 - Test Game.7z"),
                "7z",
                DatName,
                CancellationToken.None
            );

            result.IsFailed.Should().BeTrue();
            result.Errors[0].Message.Should().Contain("CRC verification");
        }

        [Test]
        public async Task ReArchiveAsync_Succeeds_WarmsScanCacheWhenProvided()
        {
            await _reArchiveStore.InitializeAsync();
            await _scanStore.InitializeAsync();
            SetupExtract(Result.Ok("/tmp/extracted.rom"));
            SetupCompress(Result.Ok());
            _fileOps
                .Setup(f => f.OpenReadAsync(It.IsAny<string>()))
                .ReturnsAsync(() => new MemoryStream(ValidRomBytes));
            DateTime lastModified = new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
            _fileOps
                .Setup(f => f.GetFileInfoAsync("/roms/0001 - Test Game.7z"))
                .ReturnsAsync((4096L, lastModified));
            Mock<IRomScanCache> scanCache = new Mock<IRomScanCache>();

            Result<MatchResult> result = await _service.ReArchiveAsync(
                Match("/roms/Old.zip"),
                ("/roms/Old.zip", "/roms/0001 - Test Game.7z"),
                "7z",
                DatName,
                CancellationToken.None,
                scanCache.Object
            );

            result.IsSuccess.Should().BeTrue();
            scanCache.Verify(
                c => c.Set("/roms/0001 - Test Game.7z", 4096L, lastModified, ValidRomCrc, null),
                Times.Once
            );
        }
    }
}
