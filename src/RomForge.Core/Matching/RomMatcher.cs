using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RomForge.Core.Models;
using RomForge.Core.Scanning;

namespace RomForge.Core.Matching
{
    public static class RomMatcher
    {
        public static MatchSummary Match(
            DatFile datFile,
            IReadOnlyList<ScannedRom> scannedRoms,
            string expectedArchiveExtension = "7z"
        )
        {
            ArgumentNullException.ThrowIfNull(datFile);
            ArgumentNullException.ThrowIfNull(scannedRoms);

            Dictionary<uint, ScannedRom> byCrc = BuildCrcIndex(scannedRoms);
            Dictionary<uint, ScannedRom> byTrimmedCrc = BuildTrimmedCrcIndex(scannedRoms);
            const string namingMask = NamingMask.DefaultMask;

            List<MatchResult> results = datFile
                .Games.Select(game =>
                    Classify(game, byCrc, byTrimmedCrc, namingMask, expectedArchiveExtension)
                )
                .ToList();

            HashSet<uint> datCrcs = datFile.Games.Select(g => g.Files.RomCrc).ToHashSet();
            List<ScannedRom> unmatched = scannedRoms
                .Where(r =>
                    !datCrcs.Contains(r.Crc)
                    && (r.TrimmedCrc is null || !datCrcs.Contains(r.TrimmedCrc.Value))
                )
                .ToList();

            return new MatchSummary(results, unmatched);
        }

        private static Dictionary<uint, ScannedRom> BuildCrcIndex(
            IReadOnlyList<ScannedRom> scannedRoms
        )
        {
            Dictionary<uint, ScannedRom> index = new(scannedRoms.Count);
            foreach (ScannedRom rom in scannedRoms)
                index.TryAdd(rom.Crc, rom);
            return index;
        }

        private static Dictionary<uint, ScannedRom> BuildTrimmedCrcIndex(
            IReadOnlyList<ScannedRom> scannedRoms
        )
        {
            Dictionary<uint, ScannedRom> index = [];
            foreach (ScannedRom rom in scannedRoms)
            {
                if (rom.TrimmedCrc.HasValue)
                    index.TryAdd(rom.TrimmedCrc.Value, rom);
            }

            return index;
        }

        private static MatchResult Classify(
            Game game,
            Dictionary<uint, ScannedRom> byCrc,
            Dictionary<uint, ScannedRom> byTrimmedCrc,
            string namingMask,
            string expectedArchiveExtension
        )
        {
            // Full CRC match — ROM is correctly sized; evaluate archive type and name independently.
            if (byCrc.TryGetValue(game.Files.RomCrc, out ScannedRom? rom))
            {
                bool isWrongArchiveType = !string.Equals(
                    rom.FileExtension,
                    expectedArchiveExtension,
                    StringComparison.OrdinalIgnoreCase
                );

                // The outer archive file name and the entry name inside the archive are two distinct
                // problems with two distinct fixes: a wrong outer name is fixed by a rename (File.Move),
                // a wrong entry name only by a re-archive that rewrites the entry. They are flagged
                // separately so each routes to the operation that can actually resolve it.
                bool isIncorrectlyNamed = false;
                bool isEntryMisnamed = false;
                if (!string.IsNullOrEmpty(namingMask))
                {
                    string expectedName = NamingMask.Expand(namingMask, game);
                    string actualName = Path.GetFileNameWithoutExtension(rom.FilePath);
                    isIncorrectlyNamed = !string.Equals(
                        actualName,
                        expectedName,
                        StringComparison.OrdinalIgnoreCase
                    );
                    isEntryMisnamed = !string.Equals(
                        rom.EntryName,
                        expectedName,
                        StringComparison.OrdinalIgnoreCase
                    );
                }

                return new MatchResult
                {
                    Game = game,
                    Status = MatchStatus.Verified,
                    ScannedRom = rom,
                    IsWrongArchiveType = isWrongArchiveType,
                    IsIncorrectlyNamed = isIncorrectlyNamed,
                    IsEntryMisnamed = isEntryMisnamed,
                };
            }

            // Trimmed CRC match — ROM is found but needs trimming; other flags not checked here.
            if (byTrimmedCrc.TryGetValue(game.Files.RomCrc, out ScannedRom? trimmedRom))
            {
                return new MatchResult
                {
                    Game = game,
                    Status = MatchStatus.Verified,
                    ScannedRom = trimmedRom,
                    IsUntrimmed = true,
                };
            }

            return new MatchResult { Game = game, Status = MatchStatus.Missing };
        }
    }
}
