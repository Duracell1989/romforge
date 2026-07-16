using System;
using System.IO;
using RomForge.Core.Matching;

namespace RomForge.Core.Operations;

public static class RomTrimmer
{
    /// <summary>
    /// Returns the source archive and target archive paths for a trim operation,
    /// or null if the result is not eligible (ROM is not untrimmed or no scanned ROM is available).
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="RomReArchiver.GetReArchiveTarget"/>, this method never returns null
    /// when <c>From</c> and <c>To</c> would be identical — the content still changes, so the
    /// caller must handle the same-path case (e.g., by using a temporary output path).
    /// </remarks>
    public static (string From, string To)? GetTrimTarget(
        MatchResult result,
        string namingMask,
        string targetExtension = "7z"
    )
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.IsUntrimmed || result.ScannedRom is null)
            return null;

        string stem = string.IsNullOrEmpty(namingMask)
            ? Path.GetFileNameWithoutExtension(result.ScannedRom.FilePath)
            : NamingMask.Expand(namingMask, result.Game);

        string dir = Path.GetDirectoryName(result.ScannedRom.FilePath) ?? string.Empty;
        string to = Path.Combine(dir, stem + "." + targetExtension);

        return (result.ScannedRom.FilePath, to);
    }
}
