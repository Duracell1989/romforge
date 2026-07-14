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
using RomForge.Core.Models;
using RomForge.Core.Services;
using RomForge.Core.UnitTests.Helpers;
using Serilog;

namespace RomForge.Core.UnitTests.Services;

[TestOf(typeof(ImageSyncService))]
public sealed class ImageSyncServiceTests
{
    private const string ImgsBase = "/imgs";
    private const string ImUrl = "http://host/imgs/";

    private Mock<IImageDownloader> _downloader = null!;
    private Mock<IRomFileOperations> _fileOps = null!;
    private ImageSyncService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _downloader = new Mock<IImageDownloader>();
        _downloader
            .Setup(d =>
                d.DownloadImageAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(() => Result.Ok());
        _fileOps = new Mock<IRomFileOperations>();
        _fileOps.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
        _service = new ImageSyncService(
            _downloader.Object,
            _fileOps.Object,
            new LoggerConfiguration().CreateLogger()
        );
    }

    private static DatFile MakeDat(string? imUrl, params int[] imageNumbers)
    {
        List<Game> games = [];
        foreach (int number in imageNumbers)
            games.Add(new Game { Title = $"Game {number}", ImageNumber = number });

        return new DatFile
        {
            Header = new DatHeader { DatName = "TestDat", NewImUrl = imUrl },
            Games = games,
        };
    }

    [Test]
    public async Task SyncMissingAsync_NoImUrl_ReturnsEmptySummaryAndDownloadsNothing()
    {
        DatFile dat = MakeDat(null, 1, 2);

        Result<ImageSyncSummary> result = await _service.SyncMissingAsync(dat, ImgsBase);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ImageSyncSummary(0, 0, 0));
        _downloader.Verify(
            d =>
                d.DownloadImageAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task SyncMissingAsync_MissingImages_DownloadsBothSlotsPerGame()
    {
        DatFile dat = MakeDat(ImUrl, 1, 2);

        Result<ImageSyncSummary> result = await _service.SyncMissingAsync(dat, ImgsBase);

        result.Value.Should().Be(new ImageSyncSummary(4, 0, 4));
    }

    [Test]
    public async Task SyncMissingAsync_BuildsUrlAndDestPathFromLayout()
    {
        DatFile dat = MakeDat(ImUrl, 501);

        await _service.SyncMissingAsync(dat, ImgsBase);

        _downloader.Verify(
            d =>
                d.DownloadImageAsync(
                    "http://host/imgs/501-1000/501a.png",
                    Path.Combine(ImgsBase, "TestDat", "501-1000", "501a.png"),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Test]
    public async Task SyncMissingAsync_ExistingImages_AreSkipped()
    {
        DatFile dat = MakeDat(ImUrl, 1);
        string existing = Path.Combine(ImgsBase, "TestDat", "1-500", "1a.png");
        _fileOps.Setup(f => f.FileExists(existing)).Returns(true);

        Result<ImageSyncSummary> result = await _service.SyncMissingAsync(dat, ImgsBase);

        result.Value.Should().Be(new ImageSyncSummary(1, 0, 1));
        _downloader.Verify(
            d => d.DownloadImageAsync(existing, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Test]
    public async Task SyncMissingAsync_DuplicateImageNumbers_DownloadedOnce()
    {
        DatFile dat = MakeDat(ImUrl, 7, 7);

        Result<ImageSyncSummary> result = await _service.SyncMissingAsync(dat, ImgsBase);

        result.Value.Total.Should().Be(2);
    }

    [Test]
    public async Task SyncMissingAsync_GamesWithoutImageNumber_AreIgnored()
    {
        DatFile dat = MakeDat(ImUrl, 0, 1);

        Result<ImageSyncSummary> result = await _service.SyncMissingAsync(dat, ImgsBase);

        result.Value.Total.Should().Be(2);
    }

    [Test]
    public async Task SyncMissingAsync_DownloadFailure_IsCountedNotFatal()
    {
        DatFile dat = MakeDat(ImUrl, 1);
        _downloader
            .Setup(d =>
                d.DownloadImageAsync(
                    It.Is<string>(u => u.EndsWith("1b.png")),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Fail("404"));

        Result<ImageSyncSummary> result = await _service.SyncMissingAsync(dat, ImgsBase);

        result.Value.Should().Be(new ImageSyncSummary(1, 1, 2));
    }

    [Test]
    public async Task SyncMissingAsync_ReportsProgressForEachImage()
    {
        DatFile dat = MakeDat(ImUrl, 1);
        List<ImageSyncProgress> reported = [];
        SyncProgress<ImageSyncProgress> progress = new SyncProgress<ImageSyncProgress>(
            reported.Add
        );

        await _service.SyncMissingAsync(dat, ImgsBase, progress);

        reported.Should().HaveCount(2);
        reported[^1].Current.Should().Be(2);
        reported[^1].Total.Should().Be(2);
    }

    [Test]
    public async Task SyncMissingAsync_CancelledBeforeStart_Throws()
    {
        DatFile dat = MakeDat(ImUrl, 1);
        using CancellationTokenSource cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => _service.SyncMissingAsync(dat, ImgsBase, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
