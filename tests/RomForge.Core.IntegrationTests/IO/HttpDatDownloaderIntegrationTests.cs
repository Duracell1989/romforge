using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.IntegrationTests.Helpers;
using RomForge.Core.IO;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.IntegrationTests.IO;

[TestOf(typeof(HttpDatDownloader))]
public sealed class HttpDatDownloaderIntegrationTests
{
    private string _tempDir = string.Empty;
    private AppDataService _appData = null!;
    private string _destDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _appData = new AppDataService(Path.Combine(_tempDir, "store"));
        _destDir = Path.Combine(_tempDir, "dest");
        Directory.CreateDirectory(_destDir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    private HttpDatDownloader MakeDownloader(HttpStatusCode statusCode, byte[] content)
    {
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(statusCode, content);
        HttpClient client = new HttpClient(handler);
        return new HttpDatDownloader(client, _appData, new LoggerConfiguration().CreateLogger());
    }

    [Test]
    public async Task DownloadDatAsync_Success_MovesFileToDestDir()
    {
        HttpDatDownloader downloader = MakeDownloader(HttpStatusCode.OK, "fake dat"u8.ToArray());

        FluentResults.Result<string> result = await downloader.DownloadDatAsync(
            "http://fake/file.zip",
            _destDir,
            "mydat.zip",
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(Path.Combine(_destDir, "mydat.zip"));
        File.Exists(result.Value).Should().BeTrue();
        Directory.GetFiles(_appData.TempPath).Should().BeEmpty();
    }

    [Test]
    public async Task DownloadDatAsync_NoFileNameHint_ExtractsNameFromUrl()
    {
        HttpDatDownloader downloader = MakeDownloader(HttpStatusCode.OK, "fake dat"u8.ToArray());

        FluentResults.Result<string> result = await downloader.DownloadDatAsync(
            "http://fake/gba.zip",
            _destDir,
            null,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(Path.Combine(_destDir, "gba.zip"));
    }

    [Test]
    public async Task DownloadDatAsync_WithProgress_ReachesOneHundred()
    {
        HttpDatDownloader downloader = MakeDownloader(HttpStatusCode.OK, new byte[1000]);
        List<int> reported = [];
        SyncProgress<int> progress = new SyncProgress<int>(p => reported.Add(p));

        FluentResults.Result<string> result = await downloader.DownloadDatAsync(
            "http://fake/gba.zip",
            _destDir,
            "gba.zip",
            progress,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        reported.Should().Contain(100);
    }

    [Test]
    public async Task DownloadDatAsync_HttpError_ReturnsFailed()
    {
        HttpDatDownloader downloader = MakeDownloader(HttpStatusCode.NotFound, []);

        FluentResults.Result<string> result = await downloader.DownloadDatAsync(
            "http://fake/gba.zip",
            _destDir,
            null,
            null,
            CancellationToken.None
        );

        result.IsFailed.Should().BeTrue();
        Directory.GetFiles(_appData.TempPath).Should().BeEmpty();
    }

    [Test]
    public async Task DownloadDatAsync_Cancellation_ThrowsAndLeavesTempDirClean()
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        await cts.CancelAsync();
        HttpDatDownloader downloader = MakeDownloader(HttpStatusCode.OK, []);

        Func<Task> act = () =>
            downloader.DownloadDatAsync("http://fake/gba.zip", _destDir, null, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(_appData.TempPath).Should().BeEmpty();
    }
}
