using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using Serilog;

namespace RomForge.Core.IO;

/// <summary>
/// Reads the latest published release of the RomForge repository from the GitHub REST API.
/// </summary>
public sealed class GitHubReleaseChecker : IReleaseChecker
{
    // The checker targets exactly one repository; this is a fixed, public GitHub API endpoint.
#pragma warning disable S1075 // URIs should not be hardcoded
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/Duracell1989/RomForge/releases/latest";
#pragma warning restore S1075
    private const string TagNameProperty = "tag_name";
    private const string HtmlUrlProperty = "html_url";

    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public GitHubReleaseChecker(HttpClient http, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _logger = logger.ForContext<GitHubReleaseChecker>();
    }

    public async Task<Result<ReleaseInfo>> FetchLatestReleaseAsync(CancellationToken ct = default)
    {
        try
        {
            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Get,
                LatestReleaseUrl
            );
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            using JsonDocument document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: ct
            );

            JsonElement root = document.RootElement;
            string? tag = root.TryGetProperty(TagNameProperty, out JsonElement tagElement)
                ? tagElement.GetString()
                : null;
            string? htmlUrl = root.TryGetProperty(HtmlUrlProperty, out JsonElement urlElement)
                ? urlElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(htmlUrl))
                return Result.Fail("The release response did not contain the expected fields.");

            return Result.Ok(new ReleaseInfo(tag, htmlUrl));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "Failed to fetch the latest release from GitHub");
            return Result.Fail($"Could not reach the update server: {ex.Message}");
        }
    }
}
