using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.IO;
using RomForge.Core.Matching;
using RomForge.Core.Models;

namespace RomForge.Core.Operations;

/// <summary>
/// Data-layer operations for the managed DAT library: reading DAT files, importing them, loading the
/// match results to display, and clearing stale (now-missing) results. View-model construction and
/// UI wiring stay with the caller.
/// </summary>
public interface IDatLibraryService
{
    /// <summary>
    /// Reads the DAT file at <paramref name="path"/>.
    /// </summary>
    Task<Result<DatFile>> ReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a DAT into the managed library and records its offline-list configuration.
    /// </summary>
    /// <returns>A success carrying the managed DAT path, or a failure if the import failed.</returns>
    Task<Result<string>> ImportAsync(
        string sourceDatPath,
        DatHeader header,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Loads the match results to display for <paramref name="datFile"/>: the persisted scan results
    /// when present, otherwise a fresh all-missing match against an empty ROM set.
    /// </summary>
    /// <returns>
    /// The results and whether they came from the persisted cache (only cached results are worth an
    /// integrity check).
    /// </returns>
    Task<(IReadOnlyList<MatchResult> Results, bool FromCache)> LoadResultsAsync(DatFile datFile);

    /// <summary>
    /// Detects persisted results whose backing files are gone, persists them as
    /// <see cref="MatchStatus.Missing"/>, and returns those cleared results. When
    /// <paramref name="romFolder"/> is set but not currently mounted, the check is skipped so a whole
    /// unavailable folder is not wrongly recorded as missing.
    /// </summary>
    /// <returns>The results that were cleared to missing, keyed by release number.</returns>
    Task<IReadOnlyList<MatchResult>> FindAndClearStaleAsync(
        string datName,
        string? romFolder,
        IReadOnlyList<MatchResult> results
    );

    /// <summary>
    /// The managed DAT file paths on disk.
    /// </summary>
    IReadOnlyList<string> GetImportedDatPaths();
}
