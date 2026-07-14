using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.IntegrationTests.Helpers;
using RomForge.Core.IO;
using Serilog;

namespace RomForge.Core.IntegrationTests.IO;

[TestOf(typeof(HttpImageDownloader))]
public sealed class HttpImageDownloaderIntegrationTests
{
    private string _tempDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    private static HttpImageDownloader MakeDownloader(HttpStatusCode statusCode, byte[] content)
    {
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(statusCode, content);
        HttpClient client = new HttpClient(handler);
        return new HttpImageDownloader(client, new LoggerConfiguration().CreateLogger());
    }

    [Test]
    public async Task DownloadImageAsync_Success_WritesFileAndCreatesDirectories()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47];
        HttpImageDownloader downloader = MakeDownloader(HttpStatusCode.OK, png);
        string dest = Path.Combine(_tempDir, "TestDat", "1-500", "1a.png");

        FluentResults.Result result = await downloader.DownloadImageAsync(
            "http://host/imgs/1-500/1a.png",
            dest,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
        (await File.ReadAllBytesAsync(dest)).Should().Equal(png);
    }

    [Test]
    public async Task DownloadImageAsync_NotFound_ReturnsFailedAndLeavesNoFile()
    {
        HttpImageDownloader downloader = MakeDownloader(HttpStatusCode.NotFound, []);
        string dest = Path.Combine(_tempDir, "TestDat", "1-500", "1b.png");

        FluentResults.Result result = await downloader.DownloadImageAsync(
            "http://host/imgs/1-500/1b.png",
            dest,
            CancellationToken.None
        );

        result.IsFailed.Should().BeTrue();
        File.Exists(dest).Should().BeFalse();
    }

    [Test]
    public async Task DownloadImageAsync_Cancellation_ThrowsAndLeavesNoFile()
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        await cts.CancelAsync();
        HttpImageDownloader downloader = MakeDownloader(HttpStatusCode.OK, [0x00]);
        string dest = Path.Combine(_tempDir, "TestDat", "1-500", "1a.png");

        Func<Task> act = () =>
            downloader.DownloadImageAsync("http://host/img.png", dest, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(dest).Should().BeFalse();
    }
}
