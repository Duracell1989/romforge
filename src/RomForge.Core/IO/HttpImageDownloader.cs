using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using Serilog;

namespace RomForge.Core.IO;

public sealed class HttpImageDownloader : IImageDownloader
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public HttpImageDownloader(HttpClient http, ILogger logger)
    {
        _http = http;
        _logger = logger.ForContext<HttpImageDownloader>();
    }

    public async Task<Result> DownloadImageAsync(
        string imageUrl,
        string destPath,
        CancellationToken ct = default
    )
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(
                imageUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            response.EnsureSuccessStatusCode();

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            await using Stream src = await response.Content.ReadAsStreamAsync(ct);
            await using FileStream dst = File.Create(destPath);
            await src.CopyToAsync(dst, ct);
            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            TryDelete(destPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(destPath);
            _logger.Debug(ex, "Failed to download image {Url}", imageUrl);
            return Result.Fail($"Image download failed: {ex.Message}");
        }
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
            _logger.Debug(ex, "Could not delete partial image {Path}", path);
        }
    }
}
