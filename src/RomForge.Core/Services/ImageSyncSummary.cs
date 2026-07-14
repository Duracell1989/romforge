namespace RomForge.Core.Services;

/// <summary>
/// Outcome of an <see cref="ImageSyncService"/> run.
/// </summary>
public sealed record ImageSyncSummary(int Downloaded, int Failed, int Total);
