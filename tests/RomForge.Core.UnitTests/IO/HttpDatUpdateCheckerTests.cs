using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using RomForge.Core.IO;
using Serilog;
using Serilog.Core;

namespace RomForge.Core.UnitTests.IO;

[TestOf(typeof(HttpDatUpdateChecker))]
public sealed class HttpDatUpdateCheckerTests
{
    [Test]
    public async Task FetchLatestVersionAsync_SuccessResponse_ReturnsBody()
    {
        HttpClient http = MakeHttpClient(HttpStatusCode.OK, "1234");
        HttpDatUpdateChecker checker = new HttpDatUpdateChecker(http, NullLogger());

        var result = await checker.FetchLatestVersionAsync("http://example.com/version.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("1234");
    }

    [Test]
    public async Task FetchLatestVersionAsync_ResponseHasWhitespace_ReturnsTrimmed()
    {
        HttpClient http = MakeHttpClient(HttpStatusCode.OK, "  567  \n");
        HttpDatUpdateChecker checker = new HttpDatUpdateChecker(http, NullLogger());

        var result = await checker.FetchLatestVersionAsync("http://example.com/version.txt");

        result.Value.Should().Be("567");
    }

    [Test]
    public async Task FetchLatestVersionAsync_NotFoundResponse_ReturnsFailed()
    {
        HttpClient http = MakeHttpClient(HttpStatusCode.NotFound, string.Empty);
        HttpDatUpdateChecker checker = new HttpDatUpdateChecker(http, NullLogger());

        var result = await checker.FetchLatestVersionAsync("http://example.com/version.txt");

        result.IsFailed.Should().BeTrue();
    }

    [Test]
    public async Task FetchLatestVersionAsync_NetworkException_ReturnsFailed()
    {
        Mock<HttpMessageHandler> handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Network unreachable"));

        HttpClient http = new HttpClient(handlerMock.Object);
        HttpDatUpdateChecker checker = new HttpDatUpdateChecker(http, NullLogger());

        var result = await checker.FetchLatestVersionAsync("http://example.com/version.txt");

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("Network unreachable");
    }

    [Test]
    public async Task FetchLatestVersionAsync_Cancelled_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        Mock<HttpMessageHandler> handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new OperationCanceledException());

        HttpClient http = new HttpClient(handlerMock.Object);
        HttpDatUpdateChecker checker = new HttpDatUpdateChecker(http, NullLogger());
        await cts.CancelAsync();

        await checker
            .Invoking(c => c.FetchLatestVersionAsync("http://example.com/version.txt", cts.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();
    }

    private static HttpClient MakeHttpClient(HttpStatusCode status, string body)
    {
        Mock<HttpMessageHandler> handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(
                new HttpResponseMessage { StatusCode = status, Content = new StringContent(body) }
            );
        return new HttpClient(handlerMock.Object);
    }

    private static Logger NullLogger() => new LoggerConfiguration().CreateLogger();
}
