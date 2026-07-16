using System;
using System.IO;
using RomForge.Core.Matching;
using RomForge.Core.Models;

namespace RomForge.Core.Operations;

public static class RomRenamer
{
    /// <summary>
    /// Returns the source and destination paths for renaming a ROM archive to match the naming
    /// mask, or <see langword="null"/> if the result is not incorrectly named.
    /// </summary>
    public static (string From, string To)? GetRenameTarget(MatchResult result, string namingMask)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (
            !result.IsIncorrectlyNamed
            || result.ScannedRom is null
            || string.IsNullOrEmpty(namingMask)
        )
            return null;

        string expectedStem = NamingMask.Expand(namingMask, result.Game);
        string ext = Path.GetExtension(result.ScannedRom.FilePath);
        string dir = Path.GetDirectoryName(result.ScannedRom.FilePath) ?? string.Empty;
        string newPath = Path.Combine(dir, expectedStem + ext);

        return result.ScannedRom.FilePath.Equals(newPath, StringComparison.OrdinalIgnoreCase)
            ? null
            : (result.ScannedRom.FilePath, newPath);
    }
}
