using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.IO;

public sealed class HttpDatDownloader : IDatDownloader
{
    private readonly HttpClient _http;
    private readonly AppDataService _appData;
    private readonly ILogger _logger;

    public HttpDatDownloader(HttpClient http, AppDataService appData, ILogger logger)
    {
        _http = http;
        _appData = appData;
        _logger = logger.ForContext<HttpDatDownloader>();
    }

    public async Task<Result<string>> DownloadDatAsync(
        string url,
        string destDir,
        string? fileName,
        IProgress<int>? progress,
        CancellationToken ct = default
    )
    {
        var tempPath = Path.Combine(_appData.TempPath, Path.GetRandomFileName());
        try
        {
            await DownloadToFileAsync(url, tempPath, progress, ct);

            var destFileName = ResolveFileName(fileName, url, "downloaded.zip");
            var destPath = Path.Combine(destDir, destFileName);
            File.Move(tempPath, destPath, overwrite: true);
            progress?.Report(100);
            return Result.Ok(destPath);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            _logger.Warning(ex, "Failed to download DAT from {Url}", url);
            return Result.Fail($"DAT download failed: {ex.Message}");
        }
    }

    private async Task DownloadToFileAsync(
        string url,
        string destPath,
        IProgress<int>? progress,
        CancellationToken ct
    )
    {
        using HttpResponseMessage response = await _http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;

        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            bytesRead += n;
            if (totalBytes > 0)
                progress?.Report((int)(bytesRead * 100 / totalBytes.Value));
        }
    }

    private static string ResolveFileName(string? hint, string url, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(hint))
            return hint;
        try
        {
            var last = new Uri(url).Segments.LastOrDefault() ?? string.Empty;
            if (!string.IsNullOrEmpty(last))
                return Uri.UnescapeDataString(last);
        }
        catch (UriFormatException)
        {
            // url was not a valid absolute URI
        }
        return fallback;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not delete temp file {Path}", path);
        }
    }
}
