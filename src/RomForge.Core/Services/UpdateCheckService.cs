using System;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.IO;
using Serilog;

namespace RomForge.Core.Services;

/// <summary>
/// Compares the running application version against the latest published release and reports
/// whether a newer version is available. Network failures are surfaced as
/// <see cref="UpdateCheckStatus.CheckFailed"/>, never thrown.
/// </summary>
public sealed class UpdateCheckService
{
    private readonly IReleaseChecker _releaseChecker;
    private readonly ILogger _logger;
    private readonly string _currentVersion;

    public UpdateCheckService(IReleaseChecker releaseChecker, ILogger logger, string currentVersion)
    {
        _releaseChecker = releaseChecker;
        _logger = logger.ForContext<UpdateCheckService>();
        _currentVersion = currentVersion;
    }

    public async Task<UpdateCheckOutcome> CheckAsync(CancellationToken ct = default)
    {
        Result<ReleaseInfo> result = await _releaseChecker.FetchLatestReleaseAsync(ct);
        if (result.IsFailed)
            return new UpdateCheckOutcome(
                UpdateCheckStatus.CheckFailed,
                _currentVersion,
                null,
                null,
                result.Errors[0].Message
            );

        ReleaseInfo latest = result.Value;
        string latestVersion = NormalizeVersion(latest.TagName);
        bool isNewer = IsNewer(latestVersion, _currentVersion);

        return new UpdateCheckOutcome(
            isNewer ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
            _currentVersion,
            latestVersion,
            latest.HtmlUrl,
            null
        );
    }

    /// <summary>
    /// Strips a leading "v"/"V" from a release tag (e.g. "v1.1.0" becomes "1.1.0").
    /// </summary>
    internal static string NormalizeVersion(string tag)
    {
        string trimmed = tag.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];
        return trimmed;
    }

    /// <summary>
    /// True when <paramref name="latest"/> is a strictly higher version than
    /// <paramref name="current"/>. If either value cannot be parsed as a version, returns false so
    /// an unparseable tag never prompts the user.
    /// </summary>
    private bool IsNewer(string latest, string current)
    {
        if (!Version.TryParse(latest, out Version? latestVersion))
        {
            _logger.Warning("Could not parse latest release tag {Tag}", latest);
            return false;
        }

        if (!Version.TryParse(current, out Version? currentVersion))
        {
            _logger.Warning("Could not parse current version {Version}", current);
            return false;
        }

        return latestVersion > currentVersion;
    }
}
