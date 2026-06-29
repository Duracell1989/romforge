using RomForge.Core.Models;
using RomForge.Core.Scanning;

namespace RomForge.Core.Matching;

public sealed record MatchResult
{
    public required Game Game { get; init; }
    public required MatchStatus Status { get; init; }
    public ScannedRom? ScannedRom { get; init; }
    public bool IsIncorrectlyNamed { get; init; }
    public bool IsWrongArchiveType { get; init; }
    public bool IsUntrimmed { get; init; }
    public bool IsReArchived { get; init; }

    public bool IsGood =>
        Status == MatchStatus.Verified
        && !IsIncorrectlyNamed
        && !IsWrongArchiveType
        && !IsUntrimmed;
}
