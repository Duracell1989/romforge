using System;
using System.Collections.Generic;
using System.IO;
using RomForge.Core.Matching;

namespace RomForge.Core.Services;

/// <summary>
/// Checks whether previously-scanned ROM files still exist on disk.
/// </summary>
public static class RomIntegrityChecker
{
    /// <summary>
    /// Returns every <see cref="MatchResult"/> whose backing file is no longer present on disk.
    /// Only <see cref="MatchStatus.Verified"/> results with a non-null <see cref="Core.Matching.MatchResult.ScannedRom"/> are checked;
    /// all others are skipped.
    /// </summary>
    public static IReadOnlyList<MatchResult> FindStaleResults(IReadOnlyList<MatchResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<MatchResult> stale = new List<MatchResult>();
        foreach (MatchResult result in results)
        {
            if (result.Status != MatchStatus.Verified || result.ScannedRom is null)
                continue;
            if (!File.Exists(result.ScannedRom.FilePath))
                stale.Add(result);
        }
        return stale;
    }
}
