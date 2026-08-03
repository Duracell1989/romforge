using RomForge.Core.Models;
using RomForge.Core.Scanning;

namespace RomForge.Core.Matching
{
    public sealed record MatchResult
    {
        public required Game Game { get; init; }
        public required MatchStatus Status { get; init; }
        public ScannedRom? ScannedRom { get; init; }
        public bool IsIncorrectlyNamed { get; init; }

        /// <summary>
        /// True when the ROM entry <em>inside</em> the archive does not match the naming mask, even
        /// though the outer archive file name may be correct. This cannot be fixed by a plain rename
        /// (which only moves the outer file) — it requires a re-archive to rewrite the entry, so it
        /// blocks <see cref="RomStatus.Good"/> and leaves the ROM <see cref="RomStatus.Verified"/>.
        /// </summary>
        public bool IsEntryMisnamed { get; init; }
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
                // A misnamed inner entry is never Good — it blocks the re-archived mark from promoting
                // the ROM to Good and leaves it Verified, so a re-archive is offered to rewrite the entry.
                if (IsReArchived && !IsEntryMisnamed)
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
}
