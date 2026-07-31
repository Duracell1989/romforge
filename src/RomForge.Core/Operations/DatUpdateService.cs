using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.IO;
using RomForge.Core.Models;
using RomForge.Core.Services;

namespace RomForge.Core.Operations;

/// <summary>
/// Default <see cref="IDatUpdateService"/>: fetches the latest version string, compares it against
/// the loaded DAT (numeric when both sides parse as integers, otherwise an ordinal string
/// comparison), and downloads the update into the managed DATs directory.
/// </summary>
public sealed class DatUpdateService : IDatUpdateService
{
    private readonly IDatUpdateChecker _updateChecker;
    private readonly IDatDownloader _downloader;
    private readonly AppDataService _appData;

    public DatUpdateService(
        IDatUpdateChecker updateChecker,
        IDatDownloader downloader,
        AppDataService appData
    )
    {
        ArgumentNullException.ThrowIfNull(updateChecker);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(appData);
        _updateChecker = updateChecker;
        _downloader = downloader;
        _appData = appData;
    }

    public async Task<Result<DatUpdateCheck>> CheckForUpdateAsync(
        DatHeader header,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(header);

        if (header.NewDatVersionUrl is null)
            return Result.Fail("No update URL is configured for this DAT.");

        Result<string> versionResult = await _updateChecker.FetchLatestVersionAsync(
            header.NewDatVersionUrl,
            cancellationToken
        );
        if (versionResult.IsFailed)
            return Result.Fail(versionResult.Errors[0].Message);

        string latest = versionResult.Value;
        bool isNewer = int.TryParse(
            latest,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int latestVersion
        )
            ? latestVersion > header.DatVersion
            : !string.Equals(
                latest,
                header.DatVersion.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal
            );

        return Result.Ok(
            new DatUpdateCheck
            {
                IsNewer = isNewer,
                LatestVersion = latest,
                CurrentVersion = header.DatVersion,
            }
        );
    }

    public async Task<Result> DownloadUpdateAsync(
        DatHeader header,
        IProgress<int>? progress,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(header);

        if (header.NewDatUrl is null)
            return Result.Fail("DAT download URL is not available.");

        Result<string> datResult = await _downloader.DownloadDatAsync(
            header.NewDatUrl,
            _appData.DatsPath,
            header.NewDatFileName,
            progress,
            cancellationToken
        );

        return datResult.IsFailed ? Result.Fail(datResult.Errors[0].Message) : Result.Ok();
    }
}
