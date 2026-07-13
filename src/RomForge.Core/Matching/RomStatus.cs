namespace RomForge.Core.Matching;

/// <summary>
/// The single mutually-exclusive status a matched ROM is displayed as, evaluated in
/// priority order (most severe issue first). This is the vocabulary produced by
/// <see cref="MatchResult.DisplayStatus"/> — the one place the classification lives —
/// and consumed by status text, colour, sorting and filtering.
/// </summary>
public enum RomStatus
{
    /// <summary>No matching ROM was found on disk.</summary>
    Missing,

    /// <summary>Matched only after trimming; the file has trailing padding.</summary>
    Untrimmed,

    /// <summary>Matched, but the archive is not in the expected format.</summary>
    WrongArchive,

    /// <summary>Matched and correctly archived, but the file name is wrong.</summary>
    IncorrectlyNamed,

    /// <summary>Verified and re-archived by RomForge — confirmed correct.</summary>
    Good,

    /// <summary>CRC-verified and otherwise correct, but not yet re-archived by RomForge.</summary>
    Verified,
}
