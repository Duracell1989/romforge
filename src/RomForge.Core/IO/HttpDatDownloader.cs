using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using Serilog;
using SharpCompress.Archives;
using RomForge.Core.Services;

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

    public async Task<Result> DownloadImagesAsync(
        string url,
        string imgsDestDir,
        IProgress<int>? progress,
        CancellationToken ct = default
    )
    {
        var tempZip = Path.Combine(_appData.TempPath, Path.GetRandomFileName() + ".zip");
        try
        {
            IProgress<int>? downloadProgress = progress is not null
                ? new Progress<int>(p => progress.Report(p / 2))
                : null;
            await DownloadToFileAsync(url, tempZip, downloadProgress, ct);

            using var archive = ArchiveFactory.OpenArchive(tempZip);
            var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            var total = entries.Count;
            var done = 0;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                var relativePath = entry.Key!
                    .Replace('/', Path.DirectorySeparatorChar)
                    .TrimStart(Path.DirectorySeparatorChar);
                var destPath = Path.Combine(imgsDestDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                await using var src = await entry.OpenEntryStreamAsync(ct);
                await using var dst = File.Create(destPath);
                await src.CopyToAsync(dst, ct);
                done++;
                progress?.Report(50 + done * 50 / Math.Max(total, 1));
            }

            TryDelete(tempZip);
            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempZip);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tempZip);
            _logger.Warning(ex, "Failed to download images from {Url}", url);
            return Result.Fail($"Image download failed: {ex.Message}");
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
