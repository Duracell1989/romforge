using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.IntegrationTests.Helpers;
using RomForge.Core.IO;
using RomForge.Core.Models;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.IntegrationTests.IO
{
    [TestOf(typeof(LocalDatImporter))]
    public sealed class LocalDatImporterIntegrationTests
    {
        private string _tempDir = string.Empty;
        private AppDataService _appData = null!;
        private LocalDatImporter _importer = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _appData = new AppDataService(Path.Combine(_tempDir, "store"));
            _importer = new LocalDatImporter(_appData, new LoggerConfiguration().CreateLogger());
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_tempDir, recursive: true);

        private static DatHeader MakeHeader(string datName, string? imFolder = null) =>
            new DatHeader { DatName = datName, ImFolder = imFolder };

        [Test]
        public async Task ImportAsync_DatOutsideStore_CopiesDatToManagedStore()
        {
            string sourceDir = Path.Combine(_tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            string sourceDat = Path.Combine(sourceDir, "test.dat");
            await File.WriteAllTextAsync(sourceDat, "<dat/>");

            FluentResults.Result<string> result = await _importer.ImportAsync(
                sourceDat,
                MakeHeader("test"),
                null,
                CancellationToken.None
            );

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(Path.Combine(_appData.DatsPath, "test.dat"));
            File.Exists(result.Value).Should().BeTrue();
        }

        [Test]
        public async Task ImportAsync_DatAlreadyInStore_SkipsCopyAndReturnsOk()
        {
            string sourceDat = Path.Combine(_appData.DatsPath, "existing.dat");
            await File.WriteAllTextAsync(sourceDat, "<dat/>");

            FluentResults.Result<string> result = await _importer.ImportAsync(
                sourceDat,
                MakeHeader("existing"),
                null,
                CancellationToken.None
            );

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(sourceDat);
        }

        [Test]
        public async Task ImportAsync_SourceFileNotFound_ReturnsFailed()
        {
            string missingDat = Path.Combine(_tempDir, "missing.dat");

            FluentResults.Result<string> result = await _importer.ImportAsync(
                missingDat,
                MakeHeader("missing"),
                null,
                CancellationToken.None
            );

            result.IsFailed.Should().BeTrue();
        }

        [Test]
        public async Task ImportAsync_WithParentImgsFolder_CopiesImagesToManagedStore()
        {
            // OfflineList layout: {parent}/dats/mydat.dat, {parent}/imgs/{datName}/
            string parent = Path.Combine(_tempDir, "ol-layout");
            string datsDir = Path.Combine(parent, "dats");
            string imgSubDir = Path.Combine(parent, "imgs", "MyDat", "0");
            Directory.CreateDirectory(datsDir);
            Directory.CreateDirectory(imgSubDir);
            string sourceDat = Path.Combine(datsDir, "mydat.dat");
            await File.WriteAllTextAsync(sourceDat, "<dat/>");
            await File.WriteAllTextAsync(Path.Combine(imgSubDir, "0001a.png"), "fake");

            FluentResults.Result<string> result = await _importer.ImportAsync(
                sourceDat,
                MakeHeader("MyDat"),
                null,
                CancellationToken.None
            );

            result.IsSuccess.Should().BeTrue();
            File.Exists(Path.Combine(_appData.ImgsPath, "MyDat", "0", "0001a.png"))
                .Should()
                .BeTrue();
        }

        [Test]
        public async Task ImportAsync_WithSameLevelImgsFolder_CopiesImagesToManagedStore()
        {
            string sourceDir = Path.Combine(_tempDir, "source");
            string imgsDir = Path.Combine(sourceDir, "imgs", "MyDat");
            Directory.CreateDirectory(imgsDir);
            string sourceDat = Path.Combine(sourceDir, "mydat.dat");
            await File.WriteAllTextAsync(sourceDat, "<dat/>");
            await File.WriteAllTextAsync(Path.Combine(imgsDir, "0001a.png"), "fake");

            FluentResults.Result<string> result = await _importer.ImportAsync(
                sourceDat,
                MakeHeader("MyDat"),
                null,
                CancellationToken.None
            );

            result.IsSuccess.Should().BeTrue();
            File.Exists(Path.Combine(_appData.ImgsPath, "MyDat", "0001a.png")).Should().BeTrue();
        }

        [Test]
        public async Task ImportAsync_NoImgsFolder_SucceedsWithNoCopiedImages()
        {
            string sourceDir = Path.Combine(_tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            string sourceDat = Path.Combine(sourceDir, "mydat.dat");
            await File.WriteAllTextAsync(sourceDat, "<dat/>");

            FluentResults.Result<string> result = await _importer.ImportAsync(
                sourceDat,
                MakeHeader("MyDat"),
                null,
                CancellationToken.None
            );

            result.IsSuccess.Should().BeTrue();
            Directory
                .GetFiles(_appData.ImgsPath, "*", SearchOption.AllDirectories)
                .Should()
                .BeEmpty();
        }

        [Test]
        public async Task ImportAsync_WithProgress_ReportsProgressForEachImage()
        {
            string sourceDir = Path.Combine(_tempDir, "source");
            string imgsDir = Path.Combine(sourceDir, "imgs", "MyDat");
            Directory.CreateDirectory(imgsDir);
            string sourceDat = Path.Combine(sourceDir, "mydat.dat");
            await File.WriteAllTextAsync(sourceDat, "<dat/>");
            for (int i = 0; i < 3; i++)
                await File.WriteAllTextAsync(Path.Combine(imgsDir, $"000{i}a.png"), "fake");

            List<ImportProgress> reported = [];
            SyncProgress<ImportProgress> progress = new SyncProgress<ImportProgress>(p =>
                reported.Add(p)
            );

            await _importer.ImportAsync(
                sourceDat,
                MakeHeader("MyDat"),
                progress,
                CancellationToken.None
            );

            // initial report (Current=0) + one per image
            reported.Should().HaveCount(4);
            reported[0].Current.Should().Be(0);
            reported[0].Total.Should().Be(3);
            reported[^1].Current.Should().Be(3);
            reported[^1].Total.Should().Be(3);
        }

        [Test]
        public async Task ImportAsync_CancellationDuringImages_StillReturnsDatOk()
        {
            string sourceDir = Path.Combine(_tempDir, "source");
            string imgsDir = Path.Combine(sourceDir, "imgs", "MyDat");
            Directory.CreateDirectory(imgsDir);
            string sourceDat = Path.Combine(sourceDir, "mydat.dat");
            await File.WriteAllTextAsync(sourceDat, "<dat/>");
            await File.WriteAllTextAsync(Path.Combine(imgsDir, "0001a.png"), "fake");

            using CancellationTokenSource cts = new CancellationTokenSource();
            await cts.CancelAsync();

            FluentResults.Result<string> result = await _importer.ImportAsync(
                sourceDat,
                MakeHeader("MyDat"),
                null,
                cts.Token
            );

            result.IsSuccess.Should().BeTrue();
            File.Exists(Path.Combine(_appData.DatsPath, "mydat.dat")).Should().BeTrue();
        }
    }
}
