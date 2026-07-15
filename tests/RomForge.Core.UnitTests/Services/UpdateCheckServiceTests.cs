using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using FluentResults;
using Moq;
using NUnit.Framework;
using RomForge.Core.IO;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.UnitTests.Services;

[TestOf(typeof(UpdateCheckService))]
public sealed class UpdateCheckServiceTests
{
    private Mock<IReleaseChecker> _releaseChecker = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _releaseChecker = new Mock<IReleaseChecker>();
        _logger = new LoggerConfiguration().CreateLogger();
    }

    private UpdateCheckService Make(string currentVersion) =>
        new UpdateCheckService(_releaseChecker.Object, _logger, currentVersion);

    private void SetupLatest(string tag) =>
        _releaseChecker
            .Setup(c => c.FetchLatestReleaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new ReleaseInfo(tag, $"https://example.com/{tag}")));

    [Test]
    public async Task CheckAsync_WhenLatestNewer_ReturnsUpdateAvailable()
    {
        SetupLatest("v1.2.0");

        UpdateCheckOutcome outcome = await Make("1.1.0").CheckAsync();

        outcome.Status.Should().Be(UpdateCheckStatus.UpdateAvailable);
        outcome.LatestVersion.Should().Be("1.2.0");
        outcome.ReleaseUrl.Should().Be("https://example.com/v1.2.0");
    }

    [Test]
    public async Task CheckAsync_WhenSameVersion_ReturnsUpToDate()
    {
        SetupLatest("v1.1.0");

        UpdateCheckOutcome outcome = await Make("1.1.0").CheckAsync();

        outcome.Status.Should().Be(UpdateCheckStatus.UpToDate);
    }

    [Test]
    public async Task CheckAsync_WhenLatestOlder_ReturnsUpToDate()
    {
        SetupLatest("v1.0.0");

        UpdateCheckOutcome outcome = await Make("1.1.0").CheckAsync();

        outcome.Status.Should().Be(UpdateCheckStatus.UpToDate);
    }

    [Test]
    public async Task CheckAsync_WhenFetchFails_ReturnsCheckFailedWithError()
    {
        _releaseChecker
            .Setup(c => c.FetchLatestReleaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<ReleaseInfo>("network down"));

        UpdateCheckOutcome outcome = await Make("1.1.0").CheckAsync();

        outcome.Status.Should().Be(UpdateCheckStatus.CheckFailed);
        outcome.Error.Should().Be("network down");
    }

    [Test]
    public async Task CheckAsync_WhenTagUnparseable_ReturnsUpToDate()
    {
        SetupLatest("nightly");

        UpdateCheckOutcome outcome = await Make("1.1.0").CheckAsync();

        outcome.Status.Should().Be(UpdateCheckStatus.UpToDate);
    }

    [Test]
    public async Task CheckAsync_AlwaysReportsCurrentVersion()
    {
        SetupLatest("v1.0.0");

        UpdateCheckOutcome outcome = await Make("1.1.0").CheckAsync();

        outcome.CurrentVersion.Should().Be("1.1.0");
    }

    [TestCase("v1.1.0", "1.1.0")]
    [TestCase("V2.0.0", "2.0.0")]
    [TestCase("1.1.0", "1.1.0")]
    [TestCase("  v1.1.0  ", "1.1.0")]
    public void NormalizeVersion_StripsLeadingVAndWhitespace(string tag, string expected) =>
        UpdateCheckService.NormalizeVersion(tag).Should().Be(expected);
}
