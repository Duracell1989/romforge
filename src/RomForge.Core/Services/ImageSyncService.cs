using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.IO;
using RomForge.Core.Models;
using Serilog;

namespace RomForge.Core.Services;

/// <summary>
/// Downloads OfflineList images that are missing on disk, one PNG at a time from the DAT's
/// <c>imURL</c> base, mirroring the local <c>imgs/&lt;folder&gt;/&lt;subfolder&gt;/&lt;n&gt;{a,b}.png</c>
/// layout. Images already present are skipped, so re-runs only fetch what is missing.
/// </summary>
public sealed class ImageSyncService
{
    private static readonly string[] Slots = ["a", "b"];

    private readonly IImageDownloader _downloader;
    private readonly IRomFileOperations _fileOps;
    private readonly ILogger _logger;

    public ImageSyncService(IImageDownloader downloader, IRomFileOperations fileOps, ILogger logger)
    {
        _downloader = downloader;
        _fileOps = fileOps;
        _logger = logger.ForContext<ImageSyncService>();
    }

    /// <summary>
    /// Downloads every image referenced by <paramref name="datFile"/> that is not already
    /// present under <paramref name="imgsBasePath"/>. Per-image failures (e.g. a game with
    /// no second image) are counted, not fatal; cancellation stops the run.
    /// </summary>
    public async Task<Result<ImageSyncSummary>> SyncMissingAsync(
        DatFile datFile,
        string imgsBasePath,
        IProgress<ImageSyncProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        string? imUrlBase = datFile.Header.NewImUrl;
        if (string.IsNullOrEmpty(imUrlBase))
            return Result.Ok(new ImageSyncSummary(0, 0, 0));

        List<(string RelativeUrl, string DestPath)> missing = CollectMissing(datFile, imgsBasePath);
        int total = missing.Count;

        int downloaded = 0;
        int failed = 0;
        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            (string relativeUrl, string destPath) = missing[i];

            Result result = await _downloader.DownloadImageAsync(
                CombineUrl(imUrlBase, relativeUrl),
                destPath,
                ct
            );

            bool ok = result.IsSuccess;
            if (ok)
                downloaded++;
            else
                failed++;

            progress?.Report(new ImageSyncProgress(i + 1, total, relativeUrl, ok));
        }

        _logger.Information(
            "Image sync complete: {Downloaded} downloaded, {Failed} failed of {Total} missing",
            downloaded,
            failed,
            total
        );
        return Result.Ok(new ImageSyncSummary(downloaded, failed, total));
    }

    private List<(string RelativeUrl, string DestPath)> CollectMissing(
        DatFile datFile,
        string imgsBasePath
    )
    {
        List<(string, string)> missing = new List<(string, string)>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            int imageNumber in datFile
                .Games.Where(g => g.ImageNumber > 0)
                .Select(g => g.ImageNumber)
        )
        {
            foreach (string slot in Slots)
            {
                string destPath = Path.Combine(
                    imgsBasePath,
                    ImagePathResolver.BuildRelativeLocalPath(datFile.Header, imageNumber, slot)
                );
                if (!seen.Add(destPath) || _fileOps.FileExists(destPath))
                    continue;

                missing.Add((ImagePathResolver.BuildRelativeUrlPath(imageNumber, slot), destPath));
            }
        }
        return missing;
    }

    private static string CombineUrl(string baseUrl, string relative) =>
        $"{baseUrl.TrimEnd('/')}/{relative}";
}
