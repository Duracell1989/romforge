namespace RomForge.Core.Services;

/// <summary>
/// Outcome of comparing the running version against the latest published release.
/// </summary>
public enum UpdateCheckStatus
{
    /// <summary>
    /// The running version is the latest, or newer than the latest published release.
    /// </summary>
    UpToDate,

    /// <summary>
    /// A newer release is available.
    /// </summary>
    UpdateAvailable,

    /// <summary>
    /// The latest release could not be determined (network or parse failure).
    /// </summary>
    CheckFailed,
}
