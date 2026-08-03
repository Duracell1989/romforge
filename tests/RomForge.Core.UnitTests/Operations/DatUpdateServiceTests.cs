using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using Moq;
using NUnit.Framework;
using RomForge.Core.IO;
using RomForge.Core.Models;
using RomForge.Core.Operations;
using RomForge.Core.Services;

namespace RomForge.Core.UnitTests.Operations
{
    [TestOf(typeof(DatUpdateService))]
    public sealed class DatUpdateServiceTests
    {
        private Mock<IDatUpdateChecker> _updateChecker = null!;
        private Mock<IDatDownloader> _downloader = null!;
        private AppDataService _appData = null!;
        private DatUpdateService _service = null!;

        [SetUp]
        public void SetUp()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            _appData = new AppDataService(root);
            _updateChecker = new Mock<IDatUpdateChecker>();
            _downloader = new Mock<IDatDownloader>();
            _service = new DatUpdateService(_updateChecker.Object, _downloader.Object, _appData);
        }

        private static DatHeader Header(
            int currentVersion = 5,
            string? versionUrl = "https://example.test/version",
            string? datUrl = "https://example.test/dat.zip"
        ) =>
            new DatHeader
            {
                DatName = "Test",
                DatVersion = currentVersion,
                NewDatVersionUrl = versionUrl,
                NewDatUrl = datUrl,
                NewDatFileName = "test.dat",
            };

        private void SetupLatest(string version) =>
            _updateChecker
                .Setup(c =>
                    c.FetchLatestVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(Result.Ok(version));

        [Test]
        public async Task CheckForUpdateAsync_NoVersionUrl_ReturnsFailure()
        {
            Result<DatUpdateCheck> result = await _service.CheckForUpdateAsync(
                Header(versionUrl: null)
            );

            result.IsFailed.Should().BeTrue();
            _updateChecker.Verify(
                c => c.FetchLatestVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never
            );
        }

        [Test]
        public async Task CheckForUpdateAsync_FetchFails_ReturnsFailure()
        {
            _updateChecker
                .Setup(c =>
                    c.FetchLatestVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(Result.Fail("network down"));

            Result<DatUpdateCheck> result = await _service.CheckForUpdateAsync(Header());

            result.IsFailed.Should().BeTrue();
            result.Errors[0].Message.Should().Contain("network down");
        }

        [Test]
        public async Task CheckForUpdateAsync_NumericHigher_ReportsNewer()
        {
            SetupLatest("6");

            Result<DatUpdateCheck> result = await _service.CheckForUpdateAsync(
                Header(currentVersion: 5)
            );

            result.IsSuccess.Should().BeTrue();
            result.Value.IsNewer.Should().BeTrue();
            result.Value.LatestVersion.Should().Be("6");
            result.Value.CurrentVersion.Should().Be(5);
        }

        [Test]
        public async Task CheckForUpdateAsync_NumericEqual_ReportsNotNewer()
        {
            SetupLatest("5");

            Result<DatUpdateCheck> result = await _service.CheckForUpdateAsync(
                Header(currentVersion: 5)
            );

            result.IsSuccess.Should().BeTrue();
            result.Value.IsNewer.Should().BeFalse();
        }

        [Test]
        public async Task CheckForUpdateAsync_NonNumericDifferent_ReportsNewer()
        {
            SetupLatest("2026-07-30");

            Result<DatUpdateCheck> result = await _service.CheckForUpdateAsync(
                Header(currentVersion: 5)
            );

            result.IsSuccess.Should().BeTrue();
            result.Value.IsNewer.Should().BeTrue();
            result.Value.LatestVersion.Should().Be("2026-07-30");
        }

        [Test]
        public async Task DownloadUpdateAsync_NoDatUrl_ReturnsFailureAndDoesNotDownload()
        {
            Result result = await _service.DownloadUpdateAsync(
                Header(datUrl: null),
                progress: null
            );

            result.IsFailed.Should().BeTrue();
            _downloader.Verify(
                d =>
                    d.DownloadDatAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string?>(),
                        It.IsAny<IProgress<int>?>(),
                        It.IsAny<CancellationToken>()
                    ),
                Times.Never
            );
        }

        [Test]
        public async Task DownloadUpdateAsync_Succeeds_DownloadsIntoDatsPath()
        {
            _downloader
                .Setup(d =>
                    d.DownloadDatAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string?>(),
                        It.IsAny<IProgress<int>?>(),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(Result.Ok("/managed/test.dat"));

            Result result = await _service.DownloadUpdateAsync(Header(), progress: null);

            result.IsSuccess.Should().BeTrue();
            _downloader.Verify(
                d =>
                    d.DownloadDatAsync(
                        "https://example.test/dat.zip",
                        _appData.DatsPath,
                        "test.dat",
                        It.IsAny<IProgress<int>?>(),
                        It.IsAny<CancellationToken>()
                    ),
                Times.Once
            );
        }
    }
}
