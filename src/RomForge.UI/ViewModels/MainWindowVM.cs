using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentResults;
using RomForge.Core.IO;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Operations;
using RomForge.Core.Scanning;
using RomForge.Core.Services;
using RomForge.UI.Services;
using Serilog;

namespace RomForge.UI.ViewModels;

public partial class MainWindowVM : VMBase
{
    private readonly IFileDialogService _fileDialogs;
    private readonly Func<string, IDatReader> _datReaderFactory;
    private readonly IRomSource _romSource;
    private readonly IRomFileOperations _fileOperations;
    private readonly IArchiveCompressor _compressor;
    private readonly IArchiveExtractor _extractor;
    private readonly IUserNotifier _notifier;
    private readonly ILogger _logger;
    private readonly AppDataService _appData;
    private readonly IDatImporter _datImporter;
    private readonly IDatUpdateChecker _updateChecker;
    private readonly IDatDownloader _downloader;
    private readonly DatConfigService _configService;
    private readonly ScanResultStore _scanResultStore;
    private readonly ReArchiveStore _reArchiveStore;
    private readonly AppPreferencesService _preferencesService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IAppLifetime _appLifetime;

    private ObservableCollection<GameRowVM>? _subscribedGames;
    private string? _unverifiedFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDatLoaded))]
    [NotifyPropertyChangedFor(nameof(StatusSummary))]
    public partial LoadedDatVM? ActiveDat { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<LoadedDatVM> LoadedDats { get; set; }

    [ObservableProperty]
    public partial GameRowVM? SelectedGame { get; set; }

    [ObservableProperty]
    private partial bool IsReArchiving { get; set; }

    [ObservableProperty]
    private partial bool IsTrimming { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReArchiveButtonLabel))]
    [NotifyPropertyChangedFor(nameof(ReArchiveAllButtonLabel))]
    public partial string ArchiveFormat { get; set; }

    public string ReArchiveButtonLabel => $"Re-Archive to {ArchiveFormat}";
    public string ReArchiveAllButtonLabel => $"Re-Archive All to {ArchiveFormat}";

    public bool IsDatLoaded => ActiveDat is not null;

    public string StatusSummary => ActiveDat?.StatusSummary ?? "No DAT loaded";

    public string MoveUnverifiedLabel => $"Move Unverified ({ActiveDat?.UnmatchedCount ?? 0})";

#pragma warning disable S107
    public MainWindowVM(
        IFileDialogService fileDialogs,
        Func<string, IDatReader> datReaderFactory,
        IRomSource romSource,
        IRomFileOperations fileOperations,
        IArchiveCompressor compressor,
        IArchiveExtractor extractor,
        IUserNotifier notifier,
        ILogger logger,
        AppDataService appData,
        IDatImporter datImporter,
        IDatUpdateChecker updateChecker,
        IDatDownloader downloader,
        DatConfigService configService,
        ScanResultStore scanResultStore,
        ReArchiveStore reArchiveStore,
        AppPreferencesService preferencesService,
        IUiDispatcher uiDispatcher,
        IAppLifetime appLifetime
    )
    {
        _fileDialogs = fileDialogs;
        _datReaderFactory = datReaderFactory;
        _romSource = romSource;
        _fileOperations = fileOperations;
        _compressor = compressor;
        _extractor = extractor;
        _notifier = notifier;
        _logger = logger.ForContext<MainWindowVM>();
        _appData = appData;
        _datImporter = datImporter;
        _updateChecker = updateChecker;
        _downloader = downloader;
        _configService = configService;
        _scanResultStore = scanResultStore;
        _reArchiveStore = reArchiveStore;
        _preferencesService = preferencesService;
        _uiDispatcher = uiDispatcher;
        _appLifetime = appLifetime;
        LoadedDats = new ObservableCollection<LoadedDatVM>();
        ArchiveFormat = "7z";
    }
#pragma warning restore S107

    partial void OnSelectedGameChanged(GameRowVM? value)
    {
        RenameSelectedCommand.NotifyCanExecuteChanged();
        ReArchiveSelectedCommand.NotifyCanExecuteChanged();
        TrimSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsReArchivingChanged(bool value)
    {
        ReArchiveSelectedCommand.NotifyCanExecuteChanged();
        ReArchiveAllCommand.NotifyCanExecuteChanged();
        RenameAllCommand.NotifyCanExecuteChanged();
        TrimAllCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTrimmingChanged(bool value)
    {
        TrimSelectedCommand.NotifyCanExecuteChanged();
        TrimAllCommand.NotifyCanExecuteChanged();
        ReArchiveSelectedCommand.NotifyCanExecuteChanged();
        ReArchiveAllCommand.NotifyCanExecuteChanged();
        RenameAllCommand.NotifyCanExecuteChanged();
    }

    partial void OnActiveDatChanged(LoadedDatVM? oldValue, LoadedDatVM? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnActiveDatPropertyChanged;
            ResubscribeGames(null);
        }
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnActiveDatPropertyChanged;
            ResubscribeGames(newValue.Games);
            _ = _preferencesService.UpdateLastActiveDatAsync(newValue.DatFile.Header.DatName);
        }
        else
        {
            _ = _preferencesService.UpdateLastActiveDatAsync(null);
        }
        SelectedGame = null;
        RenameAllCommand.NotifyCanExecuteChanged();
        ReArchiveAllCommand.NotifyCanExecuteChanged();
        TrimAllCommand.NotifyCanExecuteChanged();
        ScanFolderCommand.NotifyCanExecuteChanged();
        RemoveDatCommand.NotifyCanExecuteChanged();
        CheckDatUpdateCommand.NotifyCanExecuteChanged();
        MoveUnverifiedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(MoveUnverifiedLabel));
    }

    private void OnActiveDatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoadedDatVM.StatusSummary))
            OnPropertyChanged(nameof(StatusSummary));

        if (e.PropertyName == nameof(LoadedDatVM.Games) && sender is LoadedDatVM dat)
        {
            ResubscribeGames(dat.Games);
            RenameAllCommand.NotifyCanExecuteChanged();
            ReArchiveAllCommand.NotifyCanExecuteChanged();
            TrimAllCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(LoadedDatVM.UnmatchedRoms))
        {
            MoveUnverifiedCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(MoveUnverifiedLabel));
        }
    }

    private void ResubscribeGames(ObservableCollection<GameRowVM>? newGames)
    {
        if (_subscribedGames is not null)
            _subscribedGames.CollectionChanged -= OnActiveDatGamesChanged;
        _subscribedGames = newGames;
        if (_subscribedGames is not null)
            _subscribedGames.CollectionChanged += OnActiveDatGamesChanged;
    }

    private void OnActiveDatGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RenameAllCommand.NotifyCanExecuteChanged();
        ReArchiveAllCommand.NotifyCanExecuteChanged();
        TrimAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task ImportDatAsync()
    {
        string? sourcePath = await _fileDialogs.PickDatFileAsync();
        if (sourcePath is null)
            return;

        Result<DatFile> readResult = await _datReaderFactory(sourcePath).ReadAsync();
        if (readResult.IsFailed)
        {
            await _notifier.NotifyErrorAsync(
                $"Could not read DAT file.\n{readResult.Errors[0].Message}"
            );
            return;
        }

        ProgressWindowVM progressVm = new ProgressWindowVM(0, isCancellable: true);
        Progress<ImportProgress> importProgress = new Progress<ImportProgress>(p =>
        {
            progressVm.Total = p.Total;
            progressVm.Current = p.Current;
            progressVm.CurrentFile = p.CurrentFile;
            progressVm.Progress = p.Total > 0 ? p.Current * 100 / p.Total : 0;
        });
        Task<Result<string>> importTask = _datImporter.ImportAsync(
            sourcePath,
            readResult.Value.Header,
            importProgress,
            progressVm.CancellationToken
        );
        await _notifier.ShowProgressAsync("Importing DAT", progressVm, importTask);

        var importResult = await importTask;
        if (importResult.IsFailed)
        {
            await _notifier.NotifyErrorAsync($"Import failed.\n{importResult.Errors[0].Message}");
            return;
        }

        await _configService.ImportFromOfflineListAsync(sourcePath, readResult.Value.Header);
        await LoadDatFromManagedPathAsync(importResult.Value);
    }

    public async Task LoadManagedDatsAsync()
    {
        await _scanResultStore.InitializeAsync();
        await _reArchiveStore.InitializeAsync();

        AppPreferences prefs = await _preferencesService.LoadAsync();
        ArchiveFormat = prefs.DefaultArchiveFormat;
        _unverifiedFolder = prefs.UnverifiedFolder;

        foreach (string path in _appData.GetImportedDatPaths())
        {
            Result<DatFile> result = await _datReaderFactory(path).ReadAsync();
            if (result.IsFailed)
            {
                _logger.Warning(
                    "Could not load managed DAT {Path}: {Error}",
                    path,
                    result.Errors[0].Message
                );
                continue;
            }

            LoadedDatVM datVm = await BuildDatVmAsync(result.Value, path);
            LoadedDats.Add(datVm);
        }

        if (LoadedDats.Count > 0)
        {
            LoadedDatVM? last = prefs.LastActiveDatName is not null
                ? LoadedDats.FirstOrDefault(d =>
                    string.Equals(
                        d.DatFile.Header.DatName,
                        prefs.LastActiveDatName,
                        StringComparison.Ordinal
                    )
                )
                : null;

            ActiveDat = last ?? LoadedDats[0];
        }

        await WarnIfUnverifiedFolderMissingAsync();
    }

    /// <summary>
    /// If a persisted unverified folder no longer exists on disk, notify the user and open
    /// Settings so they can choose a new one. The stale path is dropped in-memory so a later
    /// "Move Unverified" falls back to prompting rather than failing on the missing folder.
    /// </summary>
    private async Task WarnIfUnverifiedFolderMissingAsync()
    {
        if (_unverifiedFolder is null || _fileOperations.DirectoryExists(_unverifiedFolder))
            return;

        await _notifier.NotifyErrorAsync(
            $"The configured unverified folder no longer exists:\n{_unverifiedFolder}\n\nPlease choose a new one in Settings."
        );
        await OpenSettingsAsync();

        if (_unverifiedFolder is not null && !_fileOperations.DirectoryExists(_unverifiedFolder))
            _unverifiedFolder = null;
    }

    private async Task LoadDatFromManagedPathAsync(string managedPath)
    {
        int existingIndex = -1;
        for (int i = 0; i < LoadedDats.Count; i++)
        {
            if (
                string.Equals(
                    LoadedDats[i].DatFilePath,
                    managedPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                existingIndex = i;
                break;
            }
        }

        Result<DatFile> result = await _datReaderFactory(managedPath).ReadAsync();
        if (result.IsFailed)
        {
            await _notifier.NotifyErrorAsync(
                $"Could not load imported DAT.\n{result.Errors[0].Message}"
            );
            return;
        }

        LoadedDatVM datVm = await BuildDatVmAsync(result.Value, managedPath);

        if (existingIndex >= 0)
            LoadedDats[existingIndex] = datVm;
        else
            LoadedDats.Add(datVm);

        ActiveDat = datVm;
    }

    private async Task<LoadedDatVM> BuildDatVmAsync(DatFile datFile, string path)
    {
        DatConfig? config = await _configService.LoadAsync(datFile.Header.DatName);
        LoadedDatVM datVm = new LoadedDatVM(datFile, path, config);
        if (config?.RomFolderPath is not null)
            datVm.RomFolder = config.RomFolderPath;

        IReadOnlyList<MatchResult> persisted = await _scanResultStore.LoadResultsAsync(
            datFile.Header.DatName,
            datFile
        );
        IReadOnlyList<MatchResult> matchResults =
            persisted.Count > 0 ? persisted : RomMatcher.Match(datVm.DatFile, []).Results;

        datVm.Games = new ObservableCollection<GameRowVM>(matchResults.Select(datVm.BuildGameRow));

        if (persisted.Count > 0)
            _ = ValidateIntegrityAsync(datVm, matchResults);

        return datVm;
    }

    private async Task ValidateIntegrityAsync(LoadedDatVM datVm, IReadOnlyList<MatchResult> results)
    {
        try
        {
            IReadOnlyList<MatchResult> stale = await Task.Run(() =>
                RomIntegrityChecker.FindStaleResults(results)
            );

            if (stale.Count == 0)
                return;

            string datName = datVm.DatFile.Header.DatName;
#pragma warning disable S3267 // async body with multiple sequential awaits cannot be expressed as a LINQ projection
            foreach (MatchResult staleResult in stale)
            {
                GameRowVM? existing = datVm.Games.FirstOrDefault(g =>
                    g.Game.ReleaseNumber == staleResult.Game.ReleaseNumber
                );
                if (existing is null)
                    continue;

                MatchResult missing = new MatchResult
                {
                    Game = staleResult.Game,
                    Status = MatchStatus.Missing,
                };
                await _scanResultStore.UpdateResultAsync(datName, missing);

                int index = datVm.Games.IndexOf(existing);
                if (index < 0)
                    continue;

                datVm.Games[index] = datVm.BuildGameRow(missing);
                if (ReferenceEquals(SelectedGame, existing))
                    SelectedGame = datVm.Games[index];
            }
#pragma warning restore S3267

            _logger.Information(
                "Integrity check for {DatName}: {Count} missing file(s) cleared",
                datName,
                stale.Count
            );
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Integrity check failed for {DatName}",
                datVm.DatFile.Header.DatName
            );
        }
    }

    [RelayCommand(CanExecute = nameof(CanScanFolder))]
    private async Task ScanFolderAsync()
    {
        if (ActiveDat is null)
            return;

        string? folder = await _fileDialogs.PickRomFolderAsync();
        if (folder is null)
            return;

        string cachePath = _appData.GetScanCachePath(folder);
        JsonRomScanCache cache = new JsonRomScanCache(cachePath);

        ProgressWindowVM progressVm = new ProgressWindowVM(0, isCancellable: true);
        Progress<ScanProgress> scanProgress = new Progress<ScanProgress>(p =>
        {
            progressVm.Total = p.Total;
            progressVm.Current = p.Completed;
            progressVm.CurrentFile = p.CurrentFile;
            progressVm.Phase = p.Phase;
            progressVm.Progress = p.Total > 0 ? p.Completed * 100 / p.Total : 0;
        });

        Task<IReadOnlyList<ScannedRom>> scanTask = RomScanner.ScanAsync(
            _romSource,
            folder,
            cache,
            scanProgress,
            progressVm.CancellationToken
        );
        await _notifier.ShowProgressAsync("Scanning ROMs", progressVm, scanTask);

        IReadOnlyList<ScannedRom> scannedRoms;
        try
        {
            scannedRoms = await scanTask;
        }
        catch (OperationCanceledException ex)
        {
            _logger.Information(ex, "Scan cancelled");
            await cache.SaveAsync();
            return;
        }

        await cache.SaveAsync();

        ActiveDat.RomFolder = folder;
        await _configService.UpdateRomFolderAsync(ActiveDat.DatFile.Header.DatName, folder);

        MatchSummary summary = RomMatcher.Match(ActiveDat.DatFile, scannedRoms);
        string datName = ActiveDat.DatFile.Header.DatName;

        HashSet<int> reArchived = await _reArchiveStore.GetReArchivedReleasesAsync(datName);
        List<MatchResult> results = summary
            .Results.Select(r =>
                reArchived.Contains(r.Game.ReleaseNumber)
                    ? new MatchResult
                    {
                        Game = r.Game,
                        Status = r.Status,
                        ScannedRom = r.ScannedRom,
                        IsIncorrectlyNamed = r.IsIncorrectlyNamed,
                        IsWrongArchiveType = r.IsWrongArchiveType,
                        IsUntrimmed = r.IsUntrimmed,
                        IsReArchived = true,
                    }
                    : r
            )
            .ToList();

        ActiveDat.UnmatchedRoms = summary.UnmatchedRoms;
        ActiveDat.Games = new ObservableCollection<GameRowVM>(
            results.Select(ActiveDat.BuildGameRow)
        );
        await _scanResultStore.SaveResultsAsync(datName, results);

        _logger.Information(
            "Scan complete: {Total} games, {Verified} verified, {Good} good, {Missing} missing, {BadName} incorrectly named, {BadArchive} wrong archive type, {Untrimmed} untrimmed, {Unmatched} unmatched",
            results.Count,
            results.Count(r => r.Status == MatchStatus.Verified),
            results.Count(r => r.IsGood),
            results.Count(r => r.Status == MatchStatus.Missing),
            results.Count(r => r.IsIncorrectlyNamed),
            results.Count(r => r.IsWrongArchiveType),
            results.Count(r => r.IsUntrimmed),
            summary.UnmatchedRoms.Count
        );
    }

    private bool CanScanFolder() => ActiveDat is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveDat))]
    private void RemoveDat()
    {
        if (ActiveDat is null)
            return;

        int index = LoadedDats.IndexOf(ActiveDat);
        LoadedDats.RemoveAt(index);
        ActiveDat = LoadedDats.Count == 0 ? null : LoadedDats[Math.Max(0, index - 1)];
    }

    private bool CanRemoveDat() => ActiveDat is not null;

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task RenameSelectedAsync()
    {
        if (SelectedGame is null || ActiveDat is null)
            return;

        (string From, string To)? target = RomRenamer.GetRenameTarget(
            new MatchResult
            {
                Game = SelectedGame.Game,
                Status = SelectedGame.Status,
                ScannedRom = SelectedGame.ScannedRom,
                IsIncorrectlyNamed = SelectedGame.IsIncorrectlyNamed,
            },
            ActiveDat.DatFile.Header.RomTitle
        );

        if (target is null)
            return;

        Result renameResult = await _fileOperations.RenameAsync(target.Value.From, target.Value.To);
        if (renameResult.IsFailed)
        {
            await _notifier.NotifyErrorAsync($"Rename failed.\n{renameResult.Errors[0].Message}");
            return;
        }

        GameRowVM snapshot = SelectedGame;
        ScannedRom updatedRom = snapshot.ScannedRom! with { FilePath = target.Value.To };
        await ReplaceSelectedGameAsync(
            new MatchResult
            {
                Game = snapshot.Game,
                Status = MatchStatus.Verified,
                ScannedRom = updatedRom,
                IsIncorrectlyNamed = false,
                IsWrongArchiveType = snapshot.IsWrongArchiveType,
                IsUntrimmed = snapshot.IsUntrimmed,
                IsReArchived = snapshot.IsReArchived,
            }
        );
    }

    private bool CanRename() => SelectedGame?.IsIncorrectlyNamed == true;

    [RelayCommand(CanExecute = nameof(CanRenameAll))]
    private async Task RenameAllAsync()
    {
        if (ActiveDat is null)
            return;

        List<GameRowVM> targets = ActiveDat.Games.Where(g => g.IsIncorrectlyNamed).ToList();

        if (targets.Count == 0)
            return;

        ProgressWindowVM progressVm = new ProgressWindowVM(targets.Count, isCancellable: false);
        Task<List<string>> operationTask = RenameAllCoreAsync(targets, progressVm);
        await _notifier.ShowProgressAsync("Renaming ROMs", progressVm, operationTask);

        List<string> errors = await operationTask;
        _logger.Information(
            "Rename all: {Succeeded}/{Total} succeeded",
            targets.Count - errors.Count,
            targets.Count
        );

        if (errors.Count > 0)
            await _notifier.NotifyErrorAsync(
                $"Rename failed for {errors.Count} file(s):\n{string.Join("\n", errors)}"
            );
    }

    private async Task<List<string>> RenameAllCoreAsync(
        List<GameRowVM> targets,
        ProgressWindowVM progress
    )
    {
        List<string> errors = new List<string>();

        for (int i = 0; i < targets.Count; i++)
        {
            GameRowVM game = targets[i];
            progress.Current = i + 1;
            progress.CurrentFile = Path.GetFileName(game.ScannedRom?.FilePath ?? string.Empty);
            progress.Progress = (i + 1) * 100 / targets.Count;

            (string From, string To)? target = RomRenamer.GetRenameTarget(
                new MatchResult
                {
                    Game = game.Game,
                    Status = game.Status,
                    ScannedRom = game.ScannedRom,
                    IsIncorrectlyNamed = game.IsIncorrectlyNamed,
                },
                ActiveDat!.DatFile.Header.RomTitle
            );

            if (target is null)
                continue;

            Result result = await _fileOperations.RenameAsync(target.Value.From, target.Value.To);
            if (result.IsFailed)
            {
                errors.Add($"{Path.GetFileName(target.Value.From)}: {result.Errors[0].Message}");
                continue;
            }

            ScannedRom updatedRom = game.ScannedRom! with { FilePath = target.Value.To };
            await ReplaceGameAsync(
                game,
                new MatchResult
                {
                    Game = game.Game,
                    Status = MatchStatus.Verified,
                    ScannedRom = updatedRom,
                    IsIncorrectlyNamed = false,
                    IsWrongArchiveType = game.IsWrongArchiveType,
                    IsUntrimmed = game.IsUntrimmed,
                    IsReArchived = game.IsReArchived,
                }
            );
        }

        return errors;
    }

    private bool CanRenameAll() =>
        !IsReArchiving
        && !IsTrimming
        && ActiveDat is not null
        && ActiveDat.Games.Any(g => g.IsIncorrectlyNamed);

    [RelayCommand(CanExecute = nameof(CanReArchive))]
    private async Task ReArchiveSelectedAsync()
    {
        if (SelectedGame is null || ActiveDat is null)
            return;

        (string From, string To)? target = RomReArchiver.GetReArchiveTarget(
            new MatchResult
            {
                Game = SelectedGame.Game,
                Status = SelectedGame.Status,
                ScannedRom = SelectedGame.ScannedRom,
                IsUntrimmed = SelectedGame.IsUntrimmed,
            },
            ActiveDat.DatFile.Header.RomTitle,
            ArchiveFormat
        );

        if (target is null)
            return;

        GameRowVM snapshotGame = SelectedGame;
        ProgressWindowVM progressVm = new ProgressWindowVM(1, isCancellable: true);
        Task<string?> operationTask = ReArchiveSelectedCoreAsync(
            snapshotGame,
            target.Value,
            progressVm
        );
        await _notifier.ShowProgressAsync(
            $"Re-Archiving to {ArchiveFormat}",
            progressVm,
            operationTask
        );

        string? error = await operationTask;
        if (error is not null)
            await _notifier.NotifyErrorAsync(error);
    }

    private async Task<string?> ReArchiveSelectedCoreAsync(
        GameRowVM game,
        (string From, string To) target,
        ProgressWindowVM progress
    )
    {
        IsReArchiving = true;
        try
        {
            progress.CurrentFile = Path.GetFileName(target.From);
            string datName = ActiveDat!.DatFile.Header.DatName;

            IProgress<int> compressionProgress = new Progress<int>(pct => progress.Progress = pct);
            (MatchResult? updated, string? error) = await ReArchiveFileAsync(
                game,
                target,
                ArchiveFormat,
                datName,
                progress.CancellationToken,
                compressionProgress
            );

            if (error is not null)
                return error;

            if (updated is not null)
                await ReplaceGameAsync(game, updated);

            return null;
        }
        catch (OperationCanceledException ex)
        {
            _logger.Information(ex, "Re-archive cancelled");
            return null;
        }
        finally
        {
            IsReArchiving = false;
        }
    }

    private async Task<(MatchResult? Updated, string? Error)> ReArchiveFileAsync(
        GameRowVM game,
        (string From, string To) target,
        string archiveFormat,
        string datName,
        CancellationToken cancellationToken,
        IProgress<int>? compressionProgress = null
    )
    {
        string? tempFile = null;
        try
        {
            Result<string> extractResult = await _extractor.ExtractToTempFileAsync(
                target.From,
                cancellationToken
            );
            if (extractResult.IsFailed)
                return (
                    null,
                    $"{Path.GetFileName(target.From)}: {extractResult.Errors[0].Message}"
                );

            tempFile = extractResult.Value;

            bool sameFile = target.From.Equals(target.To, StringComparison.OrdinalIgnoreCase);
            if (sameFile)
            {
                Result preDeleteResult = await _fileOperations.DeleteAsync(target.From);
                if (preDeleteResult.IsFailed)
                    return (
                        null,
                        $"Could not replace original: {Path.GetFileName(target.From)}: {preDeleteResult.Errors[0].Message}"
                    );
            }

            Result compressResult = await _compressor.CompressAsync(
                tempFile,
                target.To,
                game.Game.RomSize,
                compressionProgress,
                archiveFormat,
                cancellationToken
            );
            if (compressResult.IsFailed)
                return (
                    null,
                    $"{Path.GetFileName(target.From)}: {compressResult.Errors[0].Message}"
                );

            if (!sameFile)
            {
                Result deleteResult = await _fileOperations.DeleteAsync(target.From);
                if (deleteResult.IsFailed)
                    return (
                        null,
                        $"Archived but could not delete original: {Path.GetFileName(target.From)}: {deleteResult.Errors[0].Message}"
                    );
            }

            await _reArchiveStore.MarkAsync(datName, game.Game.ReleaseNumber);

            MatchResult updatedMatch = new MatchResult
            {
                Game = game.Game,
                Status = MatchStatus.Verified,
                ScannedRom = game.ScannedRom! with
                {
                    FilePath = target.To,
                    FileExtension = archiveFormat,
                },
                IsIncorrectlyNamed = false,
                IsWrongArchiveType = false,
                IsUntrimmed = game.IsUntrimmed,
                IsReArchived = true,
            };

            await _scanResultStore.UpdateResultAsync(datName, updatedMatch);
            return (updatedMatch, null);
        }
        finally
        {
            if (tempFile is not null && File.Exists(tempFile))
                await _fileOperations.DeleteAsync(tempFile);
        }
    }

    private bool CanReArchive() =>
        !IsReArchiving
        && !IsTrimming
        && SelectedGame?.Status == MatchStatus.Verified
        && !SelectedGame.IsUntrimmed
        && !SelectedGame.IsGood
        && _compressor.IsAvailable;

    [RelayCommand(CanExecute = nameof(CanReArchiveAll))]
    private async Task ReArchiveAllAsync()
    {
        if (ActiveDat is null)
            return;

        List<GameRowVM> targets = ActiveDat
            .Games.Where(g => g.Status == MatchStatus.Verified && !g.IsUntrimmed && !g.IsGood)
            .ToList();

        if (targets.Count == 0)
            return;

        int maxConcurrency = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        BatchProgressWindowVM progressVm = new BatchProgressWindowVM(
            targets.Count,
            maxConcurrency,
            isCancellable: true
        );
        Task<List<string>> operationTask = ReArchiveAllCoreAsync(
            targets,
            progressVm,
            maxConcurrency
        );
        await _notifier.ShowBatchProgressAsync(
            $"Re-Archiving ROMs to {ArchiveFormat}",
            progressVm,
            operationTask
        );

        List<string> errors = await operationTask;
        _logger.Information(
            "Re-archive all: {Succeeded}/{Total} succeeded",
            targets.Count - errors.Count,
            targets.Count
        );

        if (errors.Count > 0)
            await _notifier.NotifyErrorAsync(
                $"Re-archive failed for {errors.Count} file(s):\n{string.Join("\n", errors)}"
            );
    }

    private async Task<List<string>> ReArchiveAllCoreAsync(
        List<GameRowVM> targets,
        BatchProgressWindowVM progress,
        int maxConcurrency
    )
    {
        IsReArchiving = true;
        List<string> errors = new List<string>();
        object errorsLock = new object();
        int completed = 0;
        SemaphoreSlim semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        ConcurrentQueue<BatchSlotVM> slotQueue = new ConcurrentQueue<BatchSlotVM>(progress.Slots);
        LoadedDatVM activeDat = ActiveDat!;
        string archiveFormat = ArchiveFormat;
        string datName = activeDat.DatFile.Header.DatName;
        string namingMask = activeDat.DatFile.Header.RomTitle;
        CancellationToken ct = progress.CancellationToken;

        async Task ProcessGameAsync(GameRowVM game)
        {
            await semaphore.WaitAsync(ct);
            try
            {
                (string From, string To)? target = RomReArchiver.GetReArchiveTarget(
                    new MatchResult
                    {
                        Game = game.Game,
                        Status = game.Status,
                        ScannedRom = game.ScannedRom,
                        IsUntrimmed = game.IsUntrimmed,
                    },
                    namingMask,
                    archiveFormat
                );

                if (target is not null)
                {
                    slotQueue.TryDequeue(out BatchSlotVM? slot);
                    slot!.FileName = Path.GetFileName(target.Value.From);
                    slot.Progress = 0;

                    IProgress<int> slotProgress = new Progress<int>(pct => slot.Progress = pct);
                    (MatchResult? updated, string? error) = await ReArchiveFileAsync(
                        game,
                        target.Value,
                        archiveFormat,
                        datName,
                        ct,
                        slotProgress
                    );

                    slot.FileName = null;
                    slot.Progress = 0;
                    slotQueue.Enqueue(slot);

                    if (error is not null)
                    {
                        lock (errorsLock)
                            errors.Add(error);
                    }
                    else if (updated is not null)
                    {
                        await UpdateGameRowOnUiThreadAsync(activeDat, game, updated);
                    }
                }

                int done = Interlocked.Increment(ref completed);
                await _uiDispatcher.InvokeAsync(() =>
                {
                    progress.Completed = done;
                });
            }
            finally
            {
                semaphore.Release();
            }
        }

        try
        {
            await Task.WhenAll(targets.Select(ProcessGameAsync));
        }
        catch (OperationCanceledException ex)
        {
            _logger.Information(
                ex,
                "Re-archive all cancelled after {Completed} of {Total}",
                completed,
                targets.Count
            );
        }
        finally
        {
            IsReArchiving = false;
        }

        return errors;
    }

    private bool CanReArchiveAll() =>
        !IsReArchiving
        && !IsTrimming
        && _compressor.IsAvailable
        && ActiveDat is not null
        && ActiveDat.Games.Any(g =>
            g.Status == MatchStatus.Verified && !g.IsUntrimmed && !g.IsGood
        );

    [RelayCommand(CanExecute = nameof(CanTrim))]
    private async Task TrimSelectedAsync()
    {
        if (SelectedGame is null || ActiveDat is null)
            return;

        (string From, string To)? target = RomTrimmer.GetTrimTarget(
            new MatchResult
            {
                Game = SelectedGame.Game,
                Status = SelectedGame.Status,
                ScannedRom = SelectedGame.ScannedRom,
                IsUntrimmed = SelectedGame.IsUntrimmed,
            },
            ActiveDat.DatFile.Header.RomTitle,
            ArchiveFormat
        );

        if (target is null)
            return;

        GameRowVM snapshotGame = SelectedGame;
        ProgressWindowVM progressVm = new ProgressWindowVM(1, isCancellable: true);
        Task<string?> operationTask = TrimSelectedCoreAsync(snapshotGame, target.Value, progressVm);
        await _notifier.ShowProgressAsync("Trimming ROM", progressVm, operationTask);

        string? error = await operationTask;
        if (error is not null)
            await _notifier.NotifyErrorAsync(error);
    }

    private async Task<string?> TrimSelectedCoreAsync(
        GameRowVM game,
        (string From, string To) target,
        ProgressWindowVM progress
    )
    {
        IsTrimming = true;
        string? tempRom = null;

        try
        {
            progress.CurrentFile = Path.GetFileName(target.From);

            Result<string> extractResult = await _extractor.ExtractToTempFileAsync(
                target.From,
                progress.CancellationToken
            );
            if (extractResult.IsFailed)
                return $"Could not extract archive.\n{extractResult.Errors[0].Message}";

            tempRom = extractResult.Value;

            Result truncateResult = await _fileOperations.TruncateAsync(tempRom, game.Game.RomSize);
            if (truncateResult.IsFailed)
                return $"Could not trim ROM.\n{truncateResult.Errors[0].Message}";

            bool samePath = target.From.Equals(target.To, StringComparison.OrdinalIgnoreCase);
            string archiveDest = samePath ? target.To + ".romforge_tmp" : target.To;

            Progress<int> progressCallback = new Progress<int>(pct => progress.Progress = pct);
            Result compressResult = await _compressor.CompressAsync(
                tempRom,
                archiveDest,
                game.Game.RomSize,
                progressCallback,
                ArchiveFormat,
                progress.CancellationToken
            );
            if (compressResult.IsFailed)
                return $"Compression failed.\n{compressResult.Errors[0].Message}";

            Result deleteResult = await _fileOperations.DeleteAsync(target.From);
            if (deleteResult.IsFailed)
                return $"Trim succeeded but the original file could not be deleted.\n{deleteResult.Errors[0].Message}";

            if (samePath)
            {
                Result renameResult = await _fileOperations.RenameAsync(archiveDest, target.To);
                if (renameResult.IsFailed)
                    return $"Trim succeeded but could not rename temp archive.\n{renameResult.Errors[0].Message}";
            }

            ScannedRom updatedRom = game.ScannedRom! with
            {
                FilePath = target.To,
                FileExtension = ArchiveFormat,
                Crc = game.Game.Files.RomCrc,
                TrimmedCrc = null,
            };
            await ReplaceGameAsync(
                game,
                new MatchResult
                {
                    Game = game.Game,
                    Status = MatchStatus.Verified,
                    ScannedRom = updatedRom,
                    IsIncorrectlyNamed = false,
                    IsWrongArchiveType = false,
                    IsUntrimmed = false,
                    IsReArchived = game.IsReArchived,
                }
            );

            return null;
        }
        catch (OperationCanceledException ex)
        {
            _logger.Information(ex, "Trim cancelled");
            return null;
        }
        finally
        {
            IsTrimming = false;
            if (tempRom is not null && File.Exists(tempRom))
                await _fileOperations.DeleteAsync(tempRom);
        }
    }

    private bool CanTrim() =>
        !IsTrimming && SelectedGame?.IsUntrimmed == true && _compressor.IsAvailable;

    [RelayCommand(CanExecute = nameof(CanTrimAll))]
    private async Task TrimAllAsync()
    {
        if (ActiveDat is null)
            return;

        List<GameRowVM> targets = ActiveDat.Games.Where(g => g.IsUntrimmed).ToList();

        if (targets.Count == 0)
            return;

        ProgressWindowVM progressVm = new ProgressWindowVM(targets.Count, isCancellable: true);
        Task<List<string>> operationTask = TrimAllCoreAsync(targets, progressVm);
        await _notifier.ShowProgressAsync("Trimming ROMs", progressVm, operationTask);

        List<string> errors = await operationTask;
        _logger.Information(
            "Trim all: {Succeeded}/{Total} succeeded",
            targets.Count - errors.Count,
            targets.Count
        );

        if (errors.Count > 0)
            await _notifier.NotifyErrorAsync(
                $"Trim failed for {errors.Count} file(s):\n{string.Join("\n", errors)}"
            );
    }

    private async Task<List<string>> TrimAllCoreAsync(
        List<GameRowVM> targets,
        ProgressWindowVM progress
    )
    {
        IsTrimming = true;
        List<string> errors = new List<string>();

        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                GameRowVM game = targets[i];
                progress.Current = i + 1;
                progress.CurrentFile = Path.GetFileName(game.ScannedRom?.FilePath ?? string.Empty);

                string? error = await TrimOneAsync(game, i, targets.Count, progress);
                if (error is not null)
                    errors.Add(error);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.Information(
                ex,
                "Trim all cancelled after {Completed} of {Total}",
                progress.Current,
                targets.Count
            );
        }
        finally
        {
            IsTrimming = false;
        }

        return errors;
    }

    private async Task<string?> TrimOneAsync(
        GameRowVM game,
        int index,
        int total,
        ProgressWindowVM progress
    )
    {
        (string From, string To)? target = RomTrimmer.GetTrimTarget(
            new MatchResult
            {
                Game = game.Game,
                Status = game.Status,
                ScannedRom = game.ScannedRom,
                IsUntrimmed = game.IsUntrimmed,
            },
            ActiveDat!.DatFile.Header.RomTitle,
            ArchiveFormat
        );

        if (target is null)
            return null;

        string? tempRom = null;
        try
        {
            Result<string> extractResult = await _extractor.ExtractToTempFileAsync(
                target.Value.From,
                progress.CancellationToken
            );
            if (extractResult.IsFailed)
                return $"{Path.GetFileName(target.Value.From)}: {extractResult.Errors[0].Message}";

            tempRom = extractResult.Value;

            Result truncateResult = await _fileOperations.TruncateAsync(tempRom, game.Game.RomSize);
            if (truncateResult.IsFailed)
                return $"{Path.GetFileName(target.Value.From)}: {truncateResult.Errors[0].Message}";

            bool samePath = target.Value.From.Equals(
                target.Value.To,
                StringComparison.OrdinalIgnoreCase
            );
            string archiveDest = samePath ? target.Value.To + ".romforge_tmp" : target.Value.To;

            int fileBase = index * 100 / total;
            int fileRange = 100 / total;
            Progress<int> progressCallback = new Progress<int>(pct =>
                progress.Progress = fileBase + pct * fileRange / 100
            );

            Result compressResult = await _compressor.CompressAsync(
                tempRom,
                archiveDest,
                game.Game.RomSize,
                progressCallback,
                ArchiveFormat,
                progress.CancellationToken
            );
            if (compressResult.IsFailed)
                return $"{Path.GetFileName(target.Value.From)}: {compressResult.Errors[0].Message}";

            Result deleteResult = await _fileOperations.DeleteAsync(target.Value.From);
            if (deleteResult.IsFailed)
                return $"Trimmed but could not delete original: {Path.GetFileName(target.Value.From)}: {deleteResult.Errors[0].Message}";

            if (samePath)
            {
                var (_, isFailed, readOnlyList) = await _fileOperations.RenameAsync(
                    archiveDest,
                    target.Value.To
                );
                if (isFailed)
                    return $"Trimmed but could not rename temp archive: {Path.GetFileName(target.Value.From)}: {readOnlyList[0].Message}";
            }

            ScannedRom updatedRom = game.ScannedRom! with
            {
                FilePath = target.Value.To,
                FileExtension = ArchiveFormat,
                Crc = game.Game.Files.RomCrc,
                TrimmedCrc = null,
            };
            await ReplaceGameAsync(
                game,
                new MatchResult
                {
                    Game = game.Game,
                    Status = MatchStatus.Verified,
                    ScannedRom = updatedRom,
                    IsIncorrectlyNamed = false,
                    IsWrongArchiveType = false,
                    IsUntrimmed = false,
                    IsReArchived = game.IsReArchived,
                }
            );

            return null;
        }
        finally
        {
            if (tempRom is not null && File.Exists(tempRom))
                await _fileOperations.DeleteAsync(tempRom);
        }
    }

    private bool CanTrimAll() =>
        !IsTrimming
        && !IsReArchiving
        && _compressor.IsAvailable
        && ActiveDat is not null
        && ActiveDat.Games.Any(g => g.IsUntrimmed);

    private async Task ReplaceGameAsync(GameRowVM original, MatchResult updatedMatch)
    {
        var updatedRow = ActiveDat!.BuildGameRow(updatedMatch);
        var index = ActiveDat.Games.IndexOf(original);
        if (index < 0)
            return;
        ActiveDat.Games[index] = updatedRow;
        if (ReferenceEquals(SelectedGame, original))
            SelectedGame = updatedRow;
        await _scanResultStore.UpdateResultAsync(ActiveDat.DatFile.Header.DatName, updatedMatch);
    }

    private async Task ReplaceSelectedGameAsync(MatchResult updatedMatch) =>
        await ReplaceGameAsync(SelectedGame!, updatedMatch);

    private async Task UpdateGameRowOnUiThreadAsync(
        LoadedDatVM activeDat,
        GameRowVM original,
        MatchResult updated
    )
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            GameRowVM updatedRow = activeDat.BuildGameRow(updated);
            int index = activeDat.Games.IndexOf(original);
            if (index >= 0)
            {
                activeDat.Games[index] = updatedRow;
                if (ReferenceEquals(SelectedGame, original))
                    SelectedGame = updatedRow;
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanMoveUnverified))]
    private async Task MoveUnverifiedAsync()
    {
        if (ActiveDat is null || ActiveDat.UnmatchedRoms.Count == 0)
            return;

        string? destFolder = _unverifiedFolder;
        if (destFolder is null)
        {
            destFolder = await _fileDialogs.PickUnverifiedDestinationAsync();
            if (destFolder is null)
                return;
        }

        List<ScannedRom> targets = ActiveDat.UnmatchedRoms.ToList();
        ProgressWindowVM progressVm = new ProgressWindowVM(targets.Count, isCancellable: true);
        Task<List<string>> moveTask = MoveUnverifiedCoreAsync(
            targets,
            destFolder,
            ActiveDat,
            progressVm
        );
        await _notifier.ShowProgressAsync("Moving Unverified Files", progressVm, moveTask);

        List<string> errors = await moveTask;
        _logger.Information(
            "Move unverified: {Moved}/{Total} moved",
            targets.Count - errors.Count,
            targets.Count
        );

        if (errors.Count > 0)
            await _notifier.NotifyErrorAsync(
                $"Move failed for {errors.Count} file(s):\n{string.Join("\n", errors)}"
            );
    }

    private async Task<List<string>> MoveUnverifiedCoreAsync(
        List<ScannedRom> targets,
        string destFolder,
        LoadedDatVM activeDat,
        ProgressWindowVM progress
    )
    {
        List<string> errors = new List<string>();
        List<ScannedRom> moved = new List<ScannedRom>();

        for (int i = 0; i < targets.Count; i++)
        {
            if (progress.CancellationToken.IsCancellationRequested)
                break;

            ScannedRom rom = targets[i];
            progress.Current = i + 1;
            progress.CurrentFile = Path.GetFileName(rom.FilePath);
            progress.Progress = (i + 1) * 100 / targets.Count;

            string destPath = Path.Combine(destFolder, Path.GetFileName(rom.FilePath));
            Result result = await _fileOperations.RenameAsync(rom.FilePath, destPath);

            if (result.IsFailed)
                errors.Add($"{Path.GetFileName(rom.FilePath)}: {result.Errors[0].Message}");
            else
                moved.Add(rom);
        }

        if (moved.Count > 0)
            activeDat.UnmatchedRoms = activeDat
                .UnmatchedRoms.Where(r => !moved.Contains(r))
                .ToList();

        return errors;
    }

    private bool CanMoveUnverified() => ActiveDat?.UnmatchedRoms.Count > 0;

    [RelayCommand(CanExecute = nameof(CanCheckDatUpdate))]
    private async Task CheckDatUpdateAsync()
    {
        if (ActiveDat is null)
            return;

        DatHeader header = ActiveDat.DatFile.Header;
        if (header.NewDatVersionUrl is null)
            return;

        Result<string> versionResult = await _updateChecker.FetchLatestVersionAsync(
            header.NewDatVersionUrl
        );
        if (versionResult.IsFailed)
        {
            await _notifier.NotifyErrorAsync(
                $"Could not check for updates.\n{versionResult.Errors[0].Message}"
            );
            return;
        }

        var latestStr = versionResult.Value;
        var isNewer = int.TryParse(latestStr, out var latestVersion)
            ? latestVersion > header.DatVersion
            : !string.Equals(latestStr, header.DatVersion.ToString(), StringComparison.Ordinal);

        if (!isNewer)
        {
            await _notifier.NotifyInfoAsync($"Already up to date (version {header.DatVersion}).");
            return;
        }

        var confirmed = await _notifier.ConfirmAsync(
            "Update Available",
            $"A newer DAT version is available (current: {header.DatVersion}, latest: {latestStr}).\n\nDownload the update now?"
        );
        if (!confirmed)
            return;

        var progressVm = new ProgressWindowVM(0, isCancellable: true);
        progressVm.CurrentFile = "Downloading DAT…";

        Task<Result> updateTask = RunDatUpdateAsync(header, progressVm);
        await _notifier.ShowProgressAsync("Updating DAT", progressVm, updateTask);

        var updateResult = await updateTask;
        if (updateResult.IsFailed)
        {
            await _notifier.NotifyErrorAsync($"Update failed.\n{updateResult.Errors[0].Message}");
            return;
        }

        await LoadDatFromManagedPathAsync(ActiveDat.DatFilePath);
    }

    private bool CanCheckDatUpdate() =>
        ActiveDat is not null && ActiveDat.DatFile.Header.NewDatVersionUrl is not null;

    private async Task<Result> RunDatUpdateAsync(DatHeader header, ProgressWindowVM progressVm)
    {
        if (header.NewDatUrl is null)
            return Result.Fail("DAT download URL is not available.");

        var hasImages = header.NewImUrl is not null;
        var datMax = hasImages ? 50 : 100;

        IProgress<int> datProgress = new Progress<int>(p =>
        {
            progressVm.Progress = p * datMax / 100;
        });

        Result<string> datResult = await _downloader.DownloadDatAsync(
            header.NewDatUrl,
            _appData.DatsPath,
            header.NewDatFileName,
            datProgress,
            progressVm.CancellationToken
        );
        if (datResult.IsFailed)
            return Result.Fail(datResult.Errors[0].Message);

        if (header.NewImUrl is null)
            return Result.Ok();

        progressVm.CurrentFile = "Downloading images…";
        IProgress<int> imgProgress = new Progress<int>(p =>
        {
            progressVm.Progress = 50 + p / 2;
        });

        return await _downloader.DownloadImagesAsync(
            header.NewImUrl,
            _appData.ImgsPath,
            imgProgress,
            progressVm.CancellationToken
        );
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        AppPreferences current = await _preferencesService.LoadAsync();
        SettingsVM settingsVm = new SettingsVM(_preferencesService, _fileDialogs, current);
        await _notifier.ShowSettingsAsync(settingsVm);

        AppPreferences updated = await _preferencesService.LoadAsync();
        ArchiveFormat = updated.DefaultArchiveFormat;
        _unverifiedFolder = updated.UnverifiedFolder;
    }

    [RelayCommand]
    private void Quit() => _appLifetime.Shutdown();
}
