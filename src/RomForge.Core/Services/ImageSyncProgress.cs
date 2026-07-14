namespace RomForge.Core.Services;

/// <summary>
/// Progress for a single image during an <see cref="ImageSyncService"/> run: the current
/// index (1-based), the total number of missing images, the image's relative path, and
/// whether it downloaded successfully.
/// </summary>
public sealed record ImageSyncProgress(int Current, int Total, string RelativePath, bool Success);
