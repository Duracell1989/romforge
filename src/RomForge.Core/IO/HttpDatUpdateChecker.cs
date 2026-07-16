using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using Serilog;

namespace RomForge.Core.IO;

public sealed class HttpDatUpdateChecker : IDatUpdateChecker
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public HttpDatUpdateChecker(HttpClient http, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _logger = logger.ForContext<HttpDatUpdateChecker>();
    }

    public async Task<Result<string>> FetchLatestVersionAsync(
        string versionUrl,
        CancellationToken ct = default
    )
    {
        try
        {
            var body = await _http.GetStringAsync(new Uri(versionUrl), ct);
            return Result.Ok(body.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "Failed to fetch DAT version from {Url}", versionUrl);
            return Result.Fail($"Could not reach update server: {ex.Message}");
        }
    }
}
