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
    // Regression for the bug the WorkingSetBudgetGate DI-singleton change (2026-08-10) fixes: a
    // re-archive and a trim each held their own WorkingSetBudgetGate instance, so together they
    // could admit jobs summing past physical memory even though each individually respected its
    // own budget. RomReArchiveService and RomTrimService must share one gate instance so a
    // concurrent re-archive and trim admit against a single real budget.
    [TestOf(typeof(RomReArchiveService))]
    [TestOf(typeof(RomTrimService))]
    public sealed class SharedWorkingSetBudgetGateTests
    {
        private static readonly byte[] RomBytes = [0x01, 0x02, 0x03, 0x04];
        private static readonly uint RomCrc = RomScanner.ComputeCrcs(RomBytes).FullCrc;

        private static MatchResult Match(string filePath) =>
            new MatchResult
            {
                Game = new Game
                {
                    ReleaseNumber = 1,
                    Title = "Test Game",
                    Files = new GameFiles { RomCrc = RomCrc, RomExtension = "gba" },
                },
                Status = MatchStatus.Verified,
                ScannedRom = new ScannedRom
                {
                    FilePath = filePath,
                    FileExtension = "zip",
                    RomExtension = "gba",
                    Crc = RomCrc,
                },
                IsWrongArchiveType = true,
            };

        [Test]
        public async Task ReArchiveAndTrim_SharingOneGate_SecondWaitsForFirstToRelease()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            AppDataService appData = new AppDataService(root);
            ILogger logger = new LoggerConfiguration().CreateLogger();

            Mock<IArchiveExtractor> extractor = new Mock<IArchiveExtractor>();
            extractor
                .Setup(e =>
                    e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(Result.Ok("/tmp/extracted.rom"));

            Mock<IRomFileOperations> fileOps = new Mock<IRomFileOperations>();
            fileOps
                .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(Result.Ok());
            fileOps.Setup(f => f.DeleteAsync(It.IsAny<string>())).ReturnsAsync(Result.Ok());
            fileOps
                .Setup(f => f.TruncateAsync(It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync(Result.Ok());
            fileOps
                .Setup(f => f.OpenReadAsync(It.IsAny<string>()))
                .ReturnsAsync(() => new MemoryStream(RomBytes));

            // Each job costs 10; a budget of 15 admits only one at a time.
            TaskCompletionSource<Result> reArchiveCompressGate = new TaskCompletionSource<Result>();
            TaskCompletionSource<Result> trimCompressGate = new TaskCompletionSource<Result>();
            Mock<IArchiveCompressor> compressor = new Mock<IArchiveCompressor>();
            compressor
                .Setup(c => c.EstimateWorkingSetBytes(It.IsAny<long>(), It.IsAny<string>()))
                .Returns(10);
            compressor
                .SetupSequence(c =>
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
                .Returns(reArchiveCompressGate.Task)
                .Returns(trimCompressGate.Task);

            ArchiveWorkspace workspace = new ArchiveWorkspace(appData, fileOps.Object, logger);
            WorkingSetBudgetGate sharedGate = new WorkingSetBudgetGate(15);
            RomReArchiveService reArchiveService = new RomReArchiveService(
                extractor.Object,
                compressor.Object,
                fileOps.Object,
                workspace,
                new ReArchiveStore(appData, logger),
                new ScanResultStore(appData, logger),
                sharedGate
            );
            RomTrimService trimService = new RomTrimService(
                extractor.Object,
                compressor.Object,
                fileOps.Object,
                workspace,
                sharedGate
            );

            Task<Result<MatchResult>> reArchiveTask = reArchiveService.ReArchiveAsync(
                Match("/roms/Old.zip"),
                ("/roms/Old.zip", "/roms/0001 - Test Game.7z"),
                "7z",
                "Test DAT",
                CancellationToken.None
            );

            Task<Result<MatchResult>> trimTask = trimService.TrimAsync(
                Match("/roms/Other.zip"),
                ("/roms/Other.zip", "/roms/0002 - Other Game.7z"),
                "7z",
                CancellationToken.None
            );

            // The re-archive holds the only 10 units the 15-unit budget can spare; the trim's
            // AcquireAsync must not have been admitted yet, so its CompressAsync is never reached.
            await Task.WhenAny(trimTask, Task.Delay(200));
            trimTask.IsCompleted.Should().BeFalse("the shared budget has no room for a second job");

            // Releasing the re-archive's lease is what lets the trim's AcquireAsync proceed.
            reArchiveCompressGate.SetResult(Result.Ok());
            await reArchiveTask;

            await Task.WhenAny(trimTask, Task.Delay(200));
            trimTask
                .IsCompleted.Should()
                .BeFalse("still waiting on its own CompressAsync to return");

            trimCompressGate.SetResult(Result.Ok());
            Result<MatchResult> trimResult = await trimTask;
            trimResult.IsSuccess.Should().BeTrue();
        }
    }
}
