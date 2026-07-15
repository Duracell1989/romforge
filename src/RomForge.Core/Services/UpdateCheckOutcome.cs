namespace RomForge.Core.Services;

/// <summary>
/// Result of an update check: the status plus the version/URL details needed to prompt the user.
/// <see cref="LatestVersion"/> and <see cref="ReleaseUrl"/> are set when a release was fetched;
/// <see cref="Error"/> is set only when the check failed.
/// </summary>
public sealed record UpdateCheckOutcome(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? Error
);
