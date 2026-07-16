using System;
using System.IO;
using RomForge.Core.Matching;
using RomForge.Core.Models;

namespace RomForge.Core.Operations;

public static class RomReArchiver
{
    public static (string From, string To)? GetReArchiveTarget(
        MatchResult result,
        string namingMask,
        string targetExtension = "7z"
    )
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status == MatchStatus.Missing || result.IsUntrimmed || result.ScannedRom is null)
            return null;

        string expectedStem = string.IsNullOrEmpty(namingMask)
            ? Path.GetFileNameWithoutExtension(result.ScannedRom.FilePath)
            : NamingMask.Expand(namingMask, result.Game);

        string dir = Path.GetDirectoryName(result.ScannedRom.FilePath) ?? string.Empty;
        string newPath = Path.Combine(dir, expectedStem + "." + targetExtension);

        return (result.ScannedRom.FilePath, newPath);
    }
}
