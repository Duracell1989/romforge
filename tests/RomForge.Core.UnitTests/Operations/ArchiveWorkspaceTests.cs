using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using Moq;
using NUnit.Framework;
using RomForge.Core.IO;
using RomForge.Core.Operations;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.UnitTests.Operations
{
    [TestOf(typeof(ArchiveWorkspace))]
    public sealed class ArchiveWorkspaceTests
    {
        private string _tempDir = string.Empty;
        private AppDataService _appData = null!;
        private Mock<IRomFileOperations> _fileOps = null!;
        private ArchiveWorkspace _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _appData = new AppDataService(_tempDir);
            _fileOps = new Mock<IRomFileOperations>();
            _sut = new ArchiveWorkspace(_appData, _fileOps.Object, new LoggerConfiguration().CreateLogger());
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_tempDir, recursive: true);

        [Test]
        public void BuildEntryName_WithRomExtension_AppendsIt()
        {
            string result = ArchiveWorkspace.BuildEntryName("/roms/0001 - Mario.7z", "gba");

            result.Should().Be("0001 - Mario.gba");
        }

        [Test]
        public void BuildEntryName_WithEmptyRomExtension_ReturnsStemUnchanged()
        {
            // A DAT entry can legitimately declare no extension for its ROM. The entry name must
            // not fabricate one — this guards against deriving the extension from the extracted
            // temp file's own name instead, which always carries a random extension of its own
            // (Path.GetRandomFileName()) and would corrupt the result for exactly this case.
            string result = ArchiveWorkspace.BuildEntryName("/roms/0001 - Mario.7z", string.Empty);

            result.Should().Be("0001 - Mario");
        }

        // --- Failed-placement recovery. The in-place path renames the original aside first, so a
        // failure here decides whether the user's ROM is recoverable; both the restore-succeeded
        // and restore-failed branches must report the right location back to the caller.

        [Test]
        public async Task PlaceWorkingArchiveAsync_WhenMoveFailsAndOriginalRestores_OmitsAsideNote()
        {
            const string rom = "/roms/0001 - Mario.7z";
            string working = Path.Combine(_appData.TempPath, "rearchive-abc.7z");
            _fileOps.Setup(f => f.RenameAsync(rom, rom + ".bak")).ReturnsAsync(Result.Ok());
            _fileOps.Setup(f => f.RenameAsync(working, rom)).ReturnsAsync(Result.Fail("volume gone"));
            _fileOps.Setup(f => f.RenameAsync(rom + ".bak", rom)).ReturnsAsync(Result.Ok());
            _fileOps
                .Setup(f => f.RenameAsync(working, It.Is<string>(p => p.StartsWith(_appData.RecoveredPath, StringComparison.Ordinal))))
                .ReturnsAsync(Result.Ok());

            (string? error, bool consumed) = await _sut.PlaceWorkingArchiveAsync(working, rom, rom);

            consumed.Should().BeTrue();
            error.Should().Contain("volume gone").And.Contain(_appData.RecoveredPath);
            // The original went back where it belongs, so pointing the user at the .bak would be wrong.
            error.Should().NotContain("The original is still safe at");
        }

        [Test]
        public async Task PlaceWorkingArchiveAsync_WhenMoveFailsAndRestoreAlsoFails_ReportsAsidePath()
        {
            const string rom = "/roms/0001 - Mario.7z";
            string working = Path.Combine(_appData.TempPath, "rearchive-abc.7z");
            _fileOps.Setup(f => f.RenameAsync(rom, rom + ".bak")).ReturnsAsync(Result.Ok());
            _fileOps.Setup(f => f.RenameAsync(working, rom)).ReturnsAsync(Result.Fail("volume gone"));
            _fileOps.Setup(f => f.RenameAsync(rom + ".bak", rom)).ReturnsAsync(Result.Fail("still gone"));
            _fileOps
                .Setup(f => f.RenameAsync(working, It.Is<string>(p => p.StartsWith(_appData.RecoveredPath, StringComparison.Ordinal))))
                .ReturnsAsync(Result.Ok());

            (string? error, bool consumed) = await _sut.PlaceWorkingArchiveAsync(working, rom, rom);

            consumed.Should().BeTrue();
            // The ROM is now only at the .bak path — the message is the user's sole pointer to it.
            error.Should().Contain("The original is still safe at").And.Contain(rom + ".bak");
        }

        [Test]
        public async Task PlaceWorkingArchiveAsync_WhenBothMoveAndRecoveryFail_KeepsWorkingArchivePath()
        {
            const string rom = "/roms/0001 - Mario.7z";
            string working = Path.Combine(_appData.TempPath, "rearchive-abc.7z");
            _fileOps.Setup(f => f.RenameAsync(rom, rom + ".bak")).ReturnsAsync(Result.Ok());
            _fileOps.Setup(f => f.RenameAsync(working, rom)).ReturnsAsync(Result.Fail("volume gone"));
            _fileOps.Setup(f => f.RenameAsync(rom + ".bak", rom)).ReturnsAsync(Result.Ok());
            _fileOps
                .Setup(f => f.RenameAsync(working, It.Is<string>(p => p.StartsWith(_appData.RecoveredPath, StringComparison.Ordinal))))
                .ReturnsAsync(Result.Fail("recovered/ unwritable"));

            (string? error, bool consumed) = await _sut.PlaceWorkingArchiveAsync(working, rom, rom);

            // Nothing moved it, so the archive is still at its temp path and that is what the
            // message must report — reporting the recovered/ path here would send the user nowhere.
            error.Should().Contain(working);
            consumed.Should().BeTrue();
        }
    }
}
