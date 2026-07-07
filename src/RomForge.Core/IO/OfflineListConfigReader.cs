using System;
using System.Collections.Generic;
using System.IO;
using FluentResults;
using RomForge.Core.Models;

namespace RomForge.Core.IO;

public sealed record OfflineListConfig
{
    public string? RomFolderPath { get; init; }
    public IReadOnlyList<LanguageBit> LanguageBits { get; init; } = [];
}

public static class OfflineListConfigReader
{
    public static Result<OfflineListConfig> Read(string iniPath)
    {
        try
        {
            var options = ParseOptionSection(iniPath);

            List<LanguageBit> bits = [];
            for (var n = 1; n <= 26; n++)
            {
                if (!options.TryGetValue($"l{n}", out var raw))
                    continue;

                var label = raw.Trim('"').Trim();
                if (!string.IsNullOrEmpty(label))
                    bits.Add(new LanguageBit(BitIndex: n, Label: label));
            }

            options.TryGetValue("RomFolder", out var romFolder);

            return Result.Ok(
                new OfflineListConfig
                {
                    RomFolderPath = string.IsNullOrEmpty(romFolder) ? null : romFolder,
                    LanguageBits = bits,
                }
            );
        }
        catch (Exception ex)
        {
            return Result.Fail($"Could not read OfflineList config: {ex.Message}");
        }
    }

    private static Dictionary<string, string> ParseOptionSection(string iniPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inOptionSection = false;

        foreach (var rawLine in File.ReadLines(iniPath))
        {
            var line = rawLine.Trim();

            if (line.StartsWith('['))
            {
                inOptionSection = line.Equals("[Option]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inOptionSection || !line.Contains('='))
                continue;

            var eq = line.IndexOf('=');
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            result[key] = value;
        }

        return result;
    }
}
