using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.IO;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.Operations;

/// <summary>
/// Default <see cref="IDatLibraryService"/>: wraps DAT reading/importing and the persisted-results
/// lifecycle so the view-model keeps only view construction and UI wiring.
/// </summary>
public sealed class DatLibraryService : IDatLibraryService
{
    private readonly Func<string, IDatReader> _datReaderFactory;
    private readonly IDatImporter _datImporter;
    private readonly DatConfigService _configService;
    private readonly ScanResultStore _scanResultStore;
    private readonly IRomFileOperations _fileOperations;
    private readonly AppDataService _appData;
    private readonly ILogger _logger;

    public DatLibraryService(
        Func<string, IDatReader> datReaderFactory,
        IDatImporter datImporter,
        DatConfigService configService,
        ScanResultStore scanResultStore,
        IRomFileOperations fileOperations,
        AppDataService appData,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(datReaderFactory);
        ArgumentNullException.ThrowIfNull(datImporter);
        ArgumentNullException.ThrowIfNull(configService);
        ArgumentNullException.ThrowIfNull(scanResultStore);
        ArgumentNullException.ThrowIfNull(fileOperations);
        ArgumentNullException.ThrowIfNull(appData);
        ArgumentNullException.ThrowIfNull(logger);
        _datReaderFactory = datReaderFactory;
        _datImporter = datImporter;
        _configService = configService;
        _scanResultStore = scanResultStore;
        _fileOperations = fileOperations;
        _appData = appData;
        _logger = logger.ForContext<DatLibraryService>();
    }

    public Task<Result<DatFile>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        return _datReaderFactory(path).ReadAsync(cancellationToken);
    }

    public async Task<Result<string>> ImportAsync(
        string sourceDatPath,
        DatHeader header,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(header);

        Result<string> importResult = await _datImporter.ImportAsync(
            sourceDatPath,
            header,
            progress,
            cancellationToken
        );
        if (importResult.IsFailed)
            return importResult;

        await _configService.ImportFromOfflineListAsync(sourceDatPath, header);
        return importResult;
    }

    public async Task<(IReadOnlyList<MatchResult> Results, bool FromCache)> LoadResultsAsync(
        DatFile datFile
    )
    {
        ArgumentNullException.ThrowIfNull(datFile);

        IReadOnlyList<MatchResult> persisted = await _scanResultStore.LoadResultsAsync(
            datFile.Header.DatName,
            datFile
        );

        return persisted.Count > 0
            ? (persisted, true)
            : (RomMatcher.Match(datFile, []).Results, false);
    }

    public async Task<IReadOnlyList<MatchResult>> FindAndClearStaleAsync(
        string datName,
        string? romFolder,
        IReadOnlyList<MatchResult> results
    )
    {
        ArgumentNullException.ThrowIfNull(results);

        // If the DAT's ROM folder is not currently available (e.g. the external drive is unmounted,
        // or has not finished mounting when the app launches), skip the check entirely. Running it
        // would report every ROM as missing and overwrite the good persisted results with "Missing",
        // forcing a full re-scan on reconnect.
        if (romFolder is not null && !_fileOperations.DirectoryExists(romFolder))
        {
            _logger.Information(
                "Skipping integrity check for {DatName}: ROM folder {Folder} is not available",
                datName,
                romFolder
            );
            return [];
        }

        IReadOnlyList<MatchResult> stale = await Task.Run(() =>
            RomIntegrityChecker.FindStaleResults(results)
        );

        if (stale.Count == 0)
            return [];

        List<MatchResult> cleared = new List<MatchResult>(stale.Count);
        foreach (MatchResult staleResult in stale)
        {
            MatchResult missing = new MatchResult
            {
                Game = staleResult.Game,
                Status = MatchStatus.Missing,
            };
            await _scanResultStore.UpdateResultAsync(datName, missing);
            cleared.Add(missing);
        }

        _logger.Information(
            "Integrity check for {DatName}: {Count} missing file(s) cleared",
            datName,
            cleared.Count
        );
        return cleared;
    }

    public IReadOnlyList<string> GetImportedDatPaths() => _appData.GetImportedDatPaths();
}
