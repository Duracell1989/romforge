using System.Collections.Generic;
using RomForge.Core.Scanning;

namespace RomForge.Core.Matching;

/// <summary>
/// The result of a full match pass: per-game results and ROMs found on disk that
/// have no corresponding entry in the DAT.
/// </summary>
public sealed record MatchSummary(
    IReadOnlyList<MatchResult> Results,
    IReadOnlyList<ScannedRom> UnmatchedRoms
);
