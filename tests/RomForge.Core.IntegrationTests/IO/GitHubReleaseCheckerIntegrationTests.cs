using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.IntegrationTests.Helpers;
using RomForge.Core.IO;
using Serilog;

namespace RomForge.Core.IntegrationTests.IO;

[TestOf(typeof(GitHubReleaseChecker))]
public sealed class GitHubReleaseCheckerIntegrationTests
{
    private static GitHubReleaseChecker MakeChecker(HttpStatusCode statusCode, string body)
    {
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(
            statusCode,
            Encoding.UTF8.GetBytes(body)
        );
        HttpClient client = new HttpClient(handler);
        return new GitHubReleaseChecker(client, new LoggerConfiguration().CreateLogger());
    }

    [Test]
    public async Task FetchLatestReleaseAsync_WhenApiReturnsRelease_ParsesTagAndUrl()
    {
        const string body =
            """{"tag_name":"v1.2.0","html_url":"https://github.com/Duracell1989/RomForge/releases/tag/v1.2.0"}""";
        GitHubReleaseChecker checker = MakeChecker(HttpStatusCode.OK, body);

        FluentResults.Result<ReleaseInfo> result = await checker.FetchLatestReleaseAsync(
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TagName.Should().Be("v1.2.0");
        result
            .Value.HtmlUrl.Should()
            .Be("https://github.com/Duracell1989/RomForge/releases/tag/v1.2.0");
    }

    [Test]
    public async Task FetchLatestReleaseAsync_WhenFieldsMissing_ReturnsFailed()
    {
        GitHubReleaseChecker checker = MakeChecker(HttpStatusCode.OK, """{"name":"no tag here"}""");

        FluentResults.Result<ReleaseInfo> result = await checker.FetchLatestReleaseAsync(
            CancellationToken.None
        );

        result.IsFailed.Should().BeTrue();
    }

    [Test]
    public async Task FetchLatestReleaseAsync_WhenServerErrors_ReturnsFailed()
    {
        GitHubReleaseChecker checker = MakeChecker(HttpStatusCode.ServiceUnavailable, string.Empty);

        FluentResults.Result<ReleaseInfo> result = await checker.FetchLatestReleaseAsync(
            CancellationToken.None
        );

        result.IsFailed.Should().BeTrue();
    }
}
