namespace RomForge.Core.Operations
{
    /// <summary>
    /// Outcome of comparing a loaded DAT's version against the latest published version.
    /// </summary>
    public sealed record DatUpdateCheck
    {
        /// <summary>
        /// Whether the published version is newer than the currently loaded one.
        /// </summary>
        public required bool IsNewer { get; init; }

        /// <summary>
        /// The latest version string as published at the version URL.
        /// </summary>
        public required string LatestVersion { get; init; }

        /// <summary>
        /// The currently loaded DAT's version.
        /// </summary>
        public required int CurrentVersion { get; init; }
    }
}
