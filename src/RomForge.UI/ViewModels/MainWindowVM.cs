using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
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

    private ObservableCollection<GameRowVM>? _subscribedGames;

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

    public IReadOnlyList<string> ArchiveFormats { get; } = ["7z", "zip"];

    public string ReArchiveButtonLabel => $"Re-Archive to {ArchiveFormat}";
    public string ReArchiveAllButtonLabel => $"Re-Archive All to {ArchiveFormat}";

    public bool IsDatLoaded => ActiveDat is not null;

    public string StatusSummary => ActiveDat?.StatusSummary ?? "No DAT loaded";

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
        ScanResultStore scanResultStore
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

    partial void OnArchiveFormatChanged(string value)
    {
        if (ActiveDat is not null)
            _ = _configService.UpdateArchiveFormatAsync(ActiveDat.DatFile.Header.DatName, value);
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
            _ = LoadArchiveFormatAsync(newValue.DatFile.Header.DatName);
        }
        else
        {
            ArchiveFormat = "7z";
        }
        SelectedGame = null;
        RenameAllCommand.NotifyCanExecuteChanged();
        ReArchiveAllCommand.NotifyCanExecuteChanged();
        TrimAllCommand.NotifyCanExecuteChanged();
        ScanFolderCommand.NotifyCanExecuteChanged();
        RemoveDatCommand.NotifyCanExecuteChanged();
        CheckDatUpdateCommand.NotifyCanExecuteChanged();
    }

    private void OnActiveDatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoadedDatVM.StatusSummary))
            OnPropertyChanged(nameof(StatusSummary));

        if (e.PropertyName == nameof(LoadedDatVM.Games) && sender is LoadedDatVM dat)
            ResubscribeGames(dat.Games);
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

    private async Task LoadArchiveFormatAsync(string datName)
    {
        DatConfig? config = await _configService.LoadAsync(datName);
        ArchiveFormat = config?.ArchiveFormat ?? "7z";
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
            await _notifier.NotifyErrorAsync(
                $"Import failed.\n{importResult.Errors[0].Message}"
            );
            return;
        }

        await _configService.ImportFromOfflineListAsync(sourcePath, readResult.Value.Header);
        await LoadDatFromManagedPathAsync(importResult.Value);
    }

    public async Task LoadManagedDatsAsync()
    {
        await _scanResultStore.InitializeAsync();

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
            ActiveDat = LoadedDats[0];
    }

    private async Task LoadDatFromManagedPathAsync(string managedPath)
    {
        int existingIndex = -1;
        for (int i = 0; i < LoadedDats.Count; i++)
        {
            if (string.Equals(LoadedDats[i].DatFilePath, managedPath, StringComparison.OrdinalIgnoreCase))
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

        IReadOnlyList<MatchResult> persisted =
            await _scanResultStore.LoadResultsAsync(datFile.Header.DatName, datFile);
        IReadOnlyList<MatchResult> matchResults =
            persisted.Count > 0 ? persisted : RomMatcher.Match(datVm.DatFile, []);

        datVm.Games = new ObservableCollection<GameRowVM>(matchResults.Select(datVm.BuildGameRow));
        return datVm;
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
            progressVm.Progress = p.Total > 0 ? p.Completed * 100 / p.Total : 0;
        });

        Task<IReadOnlyList<ScannedRom>> scanTask = RomScanner.ScanAsync(
            _romSource, folder, cache, scanProgress, progressVm.CancellationToken
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
            return;
        }

        await cache.SaveAsync();

        ActiveDat.RomFolder = folder;
        await _configService.UpdateRomFolderAsync(ActiveDat.DatFile.Header.DatName, folder);

        List<MatchResult> matchResults = RomMatcher.Match(ActiveDat.DatFile, scannedRoms);
        ActiveDat.Games = new ObservableCollection<GameRowVM>(
            matchResults.Select(ActiveDat.BuildGameRow)
        );
        await _scanResultStore.SaveResultsAsync(ActiveDat.DatFile.Header.DatName, matchResults);

        _logger.Information(
            "Scan complete: {Total} games, {Verified} verified, {Missing} missing, {BadName} incorrectly named, {BadArchive} wrong archive type, {Untrimmed} untrimmed",
            matchResults.Count,
            matchResults.Count(r => r.Status == MatchStatus.Verified),
            matchResults.Count(r => r.Status == MatchStatus.Missing),
            matchResults.Count(r => r.Status == MatchStatus.IncorrectlyNamed),
            matchResults.Count(r => r.Status == MatchStatus.WrongArchiveType),
            matchResults.Count(r => r.Status == MatchStatus.Untrimmed)
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

        ScannedRom updatedRom = SelectedGame.ScannedRom! with { FilePath = target.Value.To };
        await ReplaceSelectedGameAsync(
            new MatchResult
            {
                Game = SelectedGame.Game,
                Status = MatchStatus.Verified,
                ScannedRom = updatedRom,
            }
        );
    }

    private bool CanRename() => SelectedGame?.Status == MatchStatus.IncorrectlyNamed;

    [RelayCommand(CanExecute = nameof(CanRenameAll))]
    private async Task RenameAllAsync()
    {
        if (ActiveDat is null)
            return;

        List<GameRowVM> targets = ActiveDat
            .Games.Where(g => g.Status == MatchStatus.IncorrectlyNamed)
            .ToList();

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
                }
            );
        }

        return errors;
    }

    private bool CanRenameAll() =>
        !IsReArchiving
        && !IsTrimming
        && ActiveDat is not null
        && ActiveDat.Games.Any(g => g.Status == MatchStatus.IncorrectlyNamed);

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
        string? tempFile = null;

        try
        {
            progress.CurrentFile = Path.GetFileName(target.From);

            Result<string> extractResult = await _extractor.ExtractToTempFileAsync(
                target.From,
                progress.CancellationToken
            );
            if (extractResult.IsFailed)
                return $"Could not extract archive.\n{extractResult.Errors[0].Message}";

            tempFile = extractResult.Value;

            Progress<int> progressCallback = new Progress<int>(pct => progress.Progress = pct);
            Result compressResult = await _compressor.CompressAsync(
                tempFile,
                target.To,
                game.Game.RomSize,
                progressCallback,
                progress.CancellationToken,
                ArchiveFormat
            );
            if (compressResult.IsFailed)
                return $"Compression failed.\n{compressResult.Errors[0].Message}";

            Result deleteResult = await _fileOperations.DeleteAsync(target.From);
            if (deleteResult.IsFailed)
                return $"Re-archive succeeded but the original file could not be deleted.\n{deleteResult.Errors[0].Message}";

            ScannedRom updatedRom = game.ScannedRom! with
            {
                FilePath = target.To,
                FileExtension = ArchiveFormat,
            };
            await ReplaceGameAsync(
                game,
                new MatchResult
                {
                    Game = game.Game,
                    Status = MatchStatus.Verified,
                    ScannedRom = updatedRom,
                }
            );

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
            if (tempFile is not null && File.Exists(tempFile))
                await _fileOperations.DeleteAsync(tempFile);
        }
    }

    private bool CanReArchive() =>
        !IsReArchiving
        && !IsTrimming
        && SelectedGame?.Status == MatchStatus.WrongArchiveType
        && _compressor.IsAvailable;

    [RelayCommand(CanExecute = nameof(CanReArchiveAll))]
    private async Task ReArchiveAllAsync()
    {
        if (ActiveDat is null)
            return;

        List<GameRowVM> targets = ActiveDat
            .Games.Where(g => g.Status == MatchStatus.WrongArchiveType)
            .ToList();

        if (targets.Count == 0)
            return;

        ProgressWindowVM progressVm = new ProgressWindowVM(targets.Count, isCancellable: true);
        Task<List<string>> operationTask = ReArchiveAllCoreAsync(targets, progressVm);
        await _notifier.ShowProgressAsync(
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
        ProgressWindowVM progress
    )
    {
        IsReArchiving = true;
        List<string> errors = new List<string>();

        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                GameRowVM game = targets[i];
                progress.Current = i + 1;
                progress.CurrentFile = Path.GetFileName(game.ScannedRom?.FilePath ?? string.Empty);

                (string From, string To)? target = RomReArchiver.GetReArchiveTarget(
                    new MatchResult
                    {
                        Game = game.Game,
                        Status = game.Status,
                        ScannedRom = game.ScannedRom,
                    },
                    ActiveDat!.DatFile.Header.RomTitle,
                    ArchiveFormat
                );

                if (target is null)
                    continue;

                string? tempFile = null;
                try
                {
                    Result<string> extractResult = await _extractor.ExtractToTempFileAsync(
                        target.Value.From,
                        progress.CancellationToken
                    );
                    if (extractResult.IsFailed)
                    {
                        errors.Add(
                            $"{Path.GetFileName(target.Value.From)}: {extractResult.Errors[0].Message}"
                        );
                        continue;
                    }

                    tempFile = extractResult.Value;

                    int fileBase = i * 100 / targets.Count;
                    int fileRange = 100 / targets.Count;
                    Progress<int> progressCallback = new Progress<int>(pct =>
                        progress.Progress = fileBase + pct * fileRange / 100
                    );

                    Result compressResult = await _compressor.CompressAsync(
                        tempFile,
                        target.Value.To,
                        game.Game.RomSize,
                        progressCallback,
                        progress.CancellationToken,
                        ArchiveFormat
                    );
                    if (compressResult.IsFailed)
                    {
                        errors.Add(
                            $"{Path.GetFileName(target.Value.From)}: {compressResult.Errors[0].Message}"
                        );
                        continue;
                    }

                    Result deleteResult = await _fileOperations.DeleteAsync(target.Value.From);
                    if (deleteResult.IsFailed)
                        errors.Add(
                            $"Archived but could not delete original: {Path.GetFileName(target.Value.From)}: {deleteResult.Errors[0].Message}"
                        );

                    ScannedRom updatedRom = game.ScannedRom! with
                    {
                        FilePath = target.Value.To,
                        FileExtension = ArchiveFormat,
                    };
                    await ReplaceGameAsync(
                        game,
                        new MatchResult
                        {
                            Game = game.Game,
                            Status = MatchStatus.Verified,
                            ScannedRom = updatedRom,
                        }
                    );
                }
                finally
                {
                    if (tempFile is not null && File.Exists(tempFile))
                        await _fileOperations.DeleteAsync(tempFile);
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.Information(
                ex,
                "Re-archive all cancelled after {Completed} of {Total}",
                progress.Current,
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
        && ActiveDat.Games.Any(g => g.Status == MatchStatus.WrongArchiveType);

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
                progress.CancellationToken,
                ArchiveFormat
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
        !IsTrimming
        && SelectedGame?.Status == MatchStatus.Untrimmed
        && _compressor.IsAvailable;

    [RelayCommand(CanExecute = nameof(CanTrimAll))]
    private async Task TrimAllAsync()
    {
        if (ActiveDat is null)
            return;

        List<GameRowVM> targets = ActiveDat
            .Games.Where(g => g.Status == MatchStatus.Untrimmed)
            .ToList();

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
            new MatchResult { Game = game.Game, Status = game.Status, ScannedRom = game.ScannedRom },
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
                progress.CancellationToken,
                ArchiveFormat
            );
            if (compressResult.IsFailed)
                return $"{Path.GetFileName(target.Value.From)}: {compressResult.Errors[0].Message}";

            Result deleteResult = await _fileOperations.DeleteAsync(target.Value.From);
            if (deleteResult.IsFailed)
                return $"Trimmed but could not delete original: {Path.GetFileName(target.Value.From)}: {deleteResult.Errors[0].Message}";

            if (samePath)
            {
                var (_, isFailed, readOnlyList) = await _fileOperations.RenameAsync(archiveDest, target.Value.To);
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
                new MatchResult { Game = game.Game, Status = MatchStatus.Verified, ScannedRom = updatedRom }
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
        && ActiveDat.Games.Any(g => g.Status == MatchStatus.Untrimmed);

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

    [RelayCommand(CanExecute = nameof(CanCheckDatUpdate))]
    private async Task CheckDatUpdateAsync()
    {
        if (ActiveDat is null)
            return;

        DatHeader header = ActiveDat.DatFile.Header;
        if (header.NewDatVersionUrl is null)
            return;

        Result<string> versionResult = await _updateChecker.FetchLatestVersionAsync(header.NewDatVersionUrl);
        if (versionResult.IsFailed)
        {
            await _notifier.NotifyErrorAsync($"Could not check for updates.\n{versionResult.Errors[0].Message}");
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
}
