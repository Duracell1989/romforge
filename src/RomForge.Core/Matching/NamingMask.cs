using System;
using System.Globalization;
using System.Numerics;
using RomForge.Core.Models;

namespace RomForge.Core.Matching;

public static class NamingMask
{
    public static string Expand(string mask, Game game)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(game);

        return mask.Replace(
                "%u",
                game.ReleaseNumber.ToString("D4", CultureInfo.InvariantCulture),
                StringComparison.Ordinal
            )
            .Replace("%n", game.Title, StringComparison.Ordinal)
            .Replace("%s", game.SourceRom ?? string.Empty, StringComparison.Ordinal)
            .Replace("%o", game.Comment ?? string.Empty, StringComparison.Ordinal)
            .Replace("%m", MultiLangMarker(game.Language), StringComparison.Ordinal);
    }

    private static string MultiLangMarker(int language)
    {
        int count = BitOperations.PopCount((uint)language);
        return count > 1 ? $"(M{count})" : string.Empty;
    }
}
