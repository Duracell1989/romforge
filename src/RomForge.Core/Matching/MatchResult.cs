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

    /// <summary>
    /// The mutually-exclusive display status, evaluated in priority order. This is the
    /// single source of truth for status text, colour, sorting and filtering.
    /// A ROM is only <see cref="RomStatus.Good"/> once RomForge has re-archived it — a
    /// freshly scanned file that merely looks correct is <see cref="RomStatus.Verified"/>.
    /// </summary>
    public RomStatus DisplayStatus
    {
        get
        {
            if (Status == MatchStatus.Missing)
                return RomStatus.Missing;
            if (IsUntrimmed)
                return RomStatus.Untrimmed;
            if (IsWrongArchiveType)
                return RomStatus.WrongArchive;
            if (IsIncorrectlyNamed)
                return RomStatus.IncorrectlyNamed;
            if (IsReArchived)
                return RomStatus.Good;
            return RomStatus.Verified;
        }
    }

    /// <summary>
    /// True only once RomForge has re-archived the ROM with its own optimal settings —
    /// equivalent to <see cref="DisplayStatus"/> being <see cref="RomStatus.Good"/>.
    /// </summary>
    public bool IsGood => DisplayStatus == RomStatus.Good;
}
