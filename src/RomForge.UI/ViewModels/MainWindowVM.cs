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

namespace RomForge.UI.ViewModels
{
    public partial class MainWindowVM : VMBase
    {
        private readonly IFileDialogService _fileDialogs;
        private readonly IDatLibraryService _datLibrary;
        private readonly IRomSource _romSource;
        private readonly IRomFileOperations _fileOperations;
        private readonly IArchiveCompressor _compressor;
        private readonly IUserNotifier _notifier;
        private readonly IUrlLauncher _urlLauncher;
        private readonly UpdateCheckService _updateCheck;
        private readonly ILogger _logger;
        private readonly AppDataService _appData;
        private readonly IDatUpdateService _datUpdateService;
        private readonly ImageSyncService _imageSync;
        private readonly DatConfigService _configService;
        private readonly ScanResultStore _scanResultStore;
        private readonly ReArchiveStore _reArchiveStore;
        private readonly AppPreferencesService _preferencesService;
        private readonly IUiDispatcher _uiDispatcher;
        private readonly IAppLifetime _appLifetime;

        private readonly IRomRenameService _renameService;
        private readonly IRomReArchiveService _reArchiveService;
        private readonly IRomTrimService _trimService;
        private readonly BatchProgressRunner _batchRunner;
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
            IDatLibraryService datLibrary,
            IRomSource romSource,
            IRomFileOperations fileOperations,
            IArchiveCompressor compressor,
            IUserNotifier notifier,
            IUrlLauncher urlLauncher,
            UpdateCheckService updateCheck,
            ILogger logger,
            AppDataService appData,
            IDatUpdateService datUpdateService,
            ImageSyncService imageSync,
            DatConfigService configService,
            ScanResultStore scanResultStore,
            ReArchiveStore reArchiveStore,
            AppPreferencesService preferencesService,
            IUiDispatcher uiDispatcher,
            IAppLifetime appLifetime,
            IRomRenameService renameService,
            IRomReArchiveService reArchiveService,
            IRomTrimService trimService
        )
        {
            _fileDialogs = fileDialogs;
            _datLibrary = datLibrary;
            _romSource = romSource;
            _fileOperations = fileOperations;
            _compressor = compressor;
            _notifier = notifier;
            _urlLauncher = urlLauncher;
            ArgumentNullException.ThrowIfNull(logger);
            _updateCheck = updateCheck;
            _logger = logger.ForContext<MainWindowVM>();
            _appData = appData;
            _datUpdateService = datUpdateService;
            _imageSync = imageSync;
            _configService = configService;
            _scanResultStore = scanResultStore;
            _reArchiveStore = reArchiveStore;
            _preferencesService = preferencesService;
            _uiDispatcher = uiDispatcher;
            _appLifetime = appLifetime;
            _renameService = renameService;
            _reArchiveService = reArchiveService;
            _trimService = trimService;
            _batchRunner = new BatchProgressRunner(_notifier, _logger);
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
            DownloadImagesCommand.NotifyCanExecuteChanged();
            MoveUnverifiedCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(MoveUnverifiedLabel));
        }

        private void OnActiveDatPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(LoadedDatVM.StatusSummary):
                    OnPropertyChanged(nameof(StatusSummary));
                    break;
                case nameof(LoadedDatVM.Games) when sender is LoadedDatVM dat:
                    ResubscribeGames(dat.Games);
                    RenameAllCommand.NotifyCanExecuteChanged();
                    ReArchiveAllCommand.NotifyCanExecuteChanged();
                    TrimAllCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(LoadedDatVM.UnmatchedRoms):
                    MoveUnverifiedCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(MoveUnverifiedLabel));
                    break;
            }
        }

        private void ResubscribeGames(ObservableCollection<GameRowVM>? newGames)
        {
            _subscribedGames?.CollectionChanged -= OnActiveDatGamesChanged;
            _subscribedGames = newGames;
            _subscribedGames?.CollectionChanged += OnActiveDatGamesChanged;
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
            var sourcePath = await _fileDialogs.PickDatFileAsync();
            if (sourcePath is null)
                return;

            var readResult = await _datLibrary.ReadAsync(sourcePath);
            if (readResult.IsFailed)
            {
                await _notifier.NotifyErrorAsync(
                    $"Could not read DAT file.\n{readResult.Errors[0].Message}"
                );
                return;
            }

            var progressVm = new ProgressWindowVM(0, isCancellable: true);
            var importProgress = new Progress<ImportProgress>(p =>
            {
                progressVm.Total = p.Total;
                progressVm.Current = p.Current;
                progressVm.CurrentFile = p.CurrentFile;
                progressVm.Progress = p.Total > 0 ? p.Current * 100 / p.Total : 0;
            });
            var importTask = _datLibrary.ImportAsync(
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

            await LoadDatFromManagedPathAsync(importResult.Value);
        }

        public async Task LoadManagedDatsAsync()
        {
            await _scanResultStore.InitializeAsync();
            await _reArchiveStore.InitializeAsync();

            var prefs = await _preferencesService.LoadAsync();
            ArchiveFormat = prefs.DefaultArchiveFormat;
            _unverifiedFolder = prefs.UnverifiedFolder;

            foreach (var path in _datLibrary.GetImportedDatPaths())
            {
                var result = await _datLibrary.ReadAsync(path);
                if (result.IsFailed)
                {
                    _logger.Warning(
                        "Could not load managed DAT {Path}: {Error}",
                        path,
                        result.Errors[0].Message
                    );
                    continue;
                }

                var datVm = await BuildDatVmAsync(result.Value, path);
                LoadedDats.Add(datVm);
            }

            if (LoadedDats.Count > 0)
            {
                var last = prefs.LastActiveDatName is not null
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

            if (prefs.CheckForUpdatesOnStartup)
                _ = RunUpdateCheckAsync(announceWhenCurrent: false);
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

            if (
                _unverifiedFolder is not null
                && !_fileOperations.DirectoryExists(_unverifiedFolder)
            )
                _unverifiedFolder = null;
        }

        private async Task LoadDatFromManagedPathAsync(string managedPath)
        {
            var existingIndex = -1;
            for (var i = 0; i < LoadedDats.Count; i++)
            {
                if (
                    !string.Equals(
                        LoadedDats[i].DatFilePath,
                        managedPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                existingIndex = i;
                break;
            }

            var result = await _datLibrary.ReadAsync(managedPath);
            if (result.IsFailed)
            {
                await _notifier.NotifyErrorAsync(
                    $"Could not load imported DAT.\n{result.Errors[0].Message}"
                );
                return;
            }

            var datVm = await BuildDatVmAsync(result.Value, managedPath);

            if (existingIndex >= 0)
                LoadedDats[existingIndex] = datVm;
            else
                LoadedDats.Add(datVm);

            ActiveDat = datVm;
        }

        private async Task<LoadedDatVM> BuildDatVmAsync(DatFile datFile, string path)
        {
            var config = await _configService.LoadAsync(datFile.Header.DatName);
            var datVm = new LoadedDatVM(datFile, path, config);
            if (config?.RomFolderPath is not null)
                datVm.RomFolder = config.RomFolderPath;

            (var matchResults, bool fromCache) = await _datLibrary.LoadResultsAsync(datFile);

            datVm.Games = new ObservableCollection<GameRowVM>(
                matchResults.Select(datVm.BuildGameRow)
            );

            if (fromCache)
                _ = ValidateIntegrityAsync(datVm, matchResults);

            return datVm;
        }

        private async Task ValidateIntegrityAsync(
            LoadedDatVM datVm,
            IReadOnlyList<MatchResult> results
        )
        {
            try
            {
                IReadOnlyList<MatchResult> cleared = await _datLibrary.FindAndClearStaleAsync(
                    datVm.DatFile.Header.DatName,
                    datVm.RomFolder,
                    results
                );

                foreach (var missing in cleared)
                {
                    var existing = datVm.Games.FirstOrDefault(g =>
                        g.Game.ReleaseNumber == missing.Game.ReleaseNumber
                    );
                    if (existing is null)
                        continue;

                    var index = datVm.Games.IndexOf(existing);
                    if (index < 0)
                        continue;

                    datVm.Games[index] = datVm.BuildGameRow(missing);
                    if (ReferenceEquals(SelectedGame, existing))
                        SelectedGame = datVm.Games[index];
                }
            }
            // CA1031: a background integrity check must never abort the app; any failure is logged
            // and swallowed so the UI keeps running.
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
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

            var cachePath = _appData.GetScanCachePath(folder);
            var cache = new JsonRomScanCache(cachePath);

            var progressVm = new ProgressWindowVM(0, isCancellable: true);
            var scanProgress = new Progress<ScanProgress>(p =>
            {
                progressVm.Total = p.Total;
                progressVm.Current = p.Completed;
                progressVm.CurrentFile = p.CurrentFile;
                progressVm.Phase = p.Phase;
                progressVm.Progress = p.Total > 0 ? p.Completed * 100 / p.Total : 0;
            });

            var scanTask = RomScanner.ScanAsync(
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

            var summary = RomMatcher.Match(ActiveDat.DatFile, scannedRoms);
            var datName = ActiveDat.DatFile.Header.DatName;

            var reArchived = await _reArchiveStore.GetReArchivedReleasesAsync(datName);
            var results = summary
                .Results.Select(r =>
                    reArchived.Contains(r.Game.ReleaseNumber)
                        ? new MatchResult
                        {
                            Game = r.Game,
                            Status = r.Status,
                            ScannedRom = r.ScannedRom,
                            IsIncorrectlyNamed = r.IsIncorrectlyNamed,
                            IsEntryMisnamed = r.IsEntryMisnamed,
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

            var index = LoadedDats.IndexOf(ActiveDat);
            LoadedDats.RemoveAt(index);
            ActiveDat = LoadedDats.Count == 0 ? null : LoadedDats[Math.Max(0, index - 1)];
        }

        private bool CanRemoveDat() => ActiveDat is not null;

        [RelayCommand(CanExecute = nameof(CanRename))]
        private async Task RenameSelectedAsync()
        {
            if (SelectedGame is null || ActiveDat is null)
                return;

            using GameRowVM snapshot = SelectedGame;
            var result = await _renameService.RenameAsync(snapshot.Result, NamingMask.DefaultMask);

            if (result.IsFailed)
            {
                await _notifier.NotifyErrorAsync($"Rename failed.\n{result.Errors[0].Message}");
                return;
            }

            if (result.Value is not null)
                await ReplaceGameAsync(snapshot, result.Value);
        }

        private bool CanRename() => SelectedGame?.IsIncorrectlyNamed == true;

        [RelayCommand(CanExecute = nameof(CanRenameAll))]
        private async Task RenameAllAsync()
        {
            if (ActiveDat is null)
                return;

            var targets = ActiveDat.Games.Where(g => g.IsIncorrectlyNamed).ToList();

            await _batchRunner.RunAsync(
                new BatchProgressOperation<GameRowVM>
                {
                    Title = "Renaming ROMs",
                    LogLabel = "Rename all",
                    FailureLabel = "Rename",
                    CompletedVerb = "succeeded",
                    Targets = targets,
                    FileName = g => g.ScannedRom?.FilePath ?? string.Empty,
                    ProcessAsync = RenameOneAsync,
                    IsCancellable = false,
                    BumpOverallProgress = true,
                }
            );
        }

        private async Task<string?> RenameOneAsync(GameRowVM game, ProgressWindowVM progress)
        {
            var result = await _renameService.RenameAsync(game.Result, NamingMask.DefaultMask);

            if (result.IsFailed)
                return $"{Path.GetFileName(game.ScannedRom?.FilePath ?? string.Empty)}: {result.Errors[0].Message}";

            if (result.Value is not null)
                await ReplaceGameAsync(game, result.Value);
            return null;
        }

        private bool CanRenameAll() =>
            !IsReArchiving
            && !IsTrimming
            && ActiveDat?.Games.Any(g => g.IsIncorrectlyNamed) == true;

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
                NamingMask.DefaultMask,
                ArchiveFormat
            );

            if (target is null)
                return;

            var snapshotGame = SelectedGame;
            var progressVm = new ProgressWindowVM(1, isCancellable: true);
            var operationTask = ReArchiveSelectedCoreAsync(snapshotGame, target.Value, progressVm);
            await _notifier.ShowProgressAsync(
                $"Re-Archiving to {ArchiveFormat}",
                progressVm,
                operationTask
            );

            var error = await operationTask;
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
                var datName = ActiveDat!.DatFile.Header.DatName;

                IProgress<int> compressionProgress = new Progress<int>(pct =>
                    progress.Progress = pct
                );
                Result<MatchResult> result = await _reArchiveService.ReArchiveAsync(
                    game.Result,
                    target,
                    ArchiveFormat,
                    datName,
                    progress.CancellationToken,
                    compressionProgress
                );

                if (result.IsFailed)
                    return result.Errors[0].Message;

                await ReplaceGameAsync(game, result.Value);
                return null;
            }
            catch (OperationCanceledException ex)
            {
                _logger.Information(ex, "Re-archive cancelled");
                return null;
            }
            // CA1031: a single file's re-archive must never abort the app or the batch; any failure
            // is logged and returned to the caller as an error string.
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.Error(ex, "Re-archive failed unexpectedly");
                return $"Re-archive failed unexpectedly: {ex.Message}";
            }
            finally
            {
                IsReArchiving = false;
            }
        }

        private bool CanReArchive() =>
            !IsReArchiving
            && !IsTrimming
            && SelectedGame is { Status: MatchStatus.Verified, IsUntrimmed: false, IsGood: false }
            && _compressor.IsAvailable;

        [RelayCommand(CanExecute = nameof(CanReArchiveAll))]
        private async Task ReArchiveAllAsync()
        {
            if (ActiveDat is null)
                return;

            var targets = ActiveDat
                .Games.Where(g => g.Status == MatchStatus.Verified && !g.IsUntrimmed && !g.IsGood)
                .ToList();

            if (targets.Count == 0)
                return;

            var maxConcurrency = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
            var progressVm = new BatchProgressWindowVM(
                targets.Count,
                maxConcurrency,
                isCancellable: true
            );
            var operationTask = ReArchiveAllCoreAsync(targets, progressVm, maxConcurrency);
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
            var errors = new List<string>();
            var errorsLock = new object();
            var completed = 0;
            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var slotQueue = new ConcurrentQueue<BatchSlotVM>(progress.Slots);
            var activeDat = ActiveDat!;
            var archiveFormat = ArchiveFormat;
            var datName = activeDat.DatFile.Header.DatName;
            const string namingMask = NamingMask.DefaultMask;
            var ct = progress.CancellationToken;

            async Task ProcessGameAsync(GameRowVM game)
            {
                await semaphore.WaitAsync(ct);
                int done = Interlocked.Increment(ref completed);
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

                    if (target is not null && slotQueue.TryDequeue(out BatchSlotVM? slot))
                    {
                        try
                        {
                            slot.FileName = Path.GetFileName(target.Value.From);
                            slot.Progress = 0;

                            IProgress<int> slotProgress = new Progress<int>(pct =>
                                slot.Progress = pct
                            );
                            Result<MatchResult> result = await _reArchiveService.ReArchiveAsync(
                                game.Result,
                                target.Value,
                                archiveFormat,
                                datName,
                                ct,
                                slotProgress
                            );

                            if (result.IsFailed)
                            {
                                lock (errorsLock)
                                    errors.Add(result.Errors[0].Message);
                            }
                            else
                            {
                                await UpdateGameRowOnUiThreadAsync(activeDat, game, result.Value);
                            }
                        }
                        finally
                        {
                            // Always return the slot, even when the re-archive throws (e.g. a
                            // cancellation). Enqueuing only on success drained the queue while the
                            // semaphore was released, so a later file dequeued nothing and threw an
                            // NRE on the slot.
                            slot.FileName = null;
                            slot.Progress = 0;
                            slotQueue.Enqueue(slot);
                        }
                    }

                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        progress.Completed = done;
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                // CA1031: one game's failure must not abort the whole batch; it is logged and
                // recorded as a per-file error while the remaining files continue.
#pragma warning disable CA1031
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _logger.Error(ex, "Re-archive failed unexpectedly for {Game}", game.Title);
                    lock (errorsLock)
                        errors.Add($"{game.Title}: {ex.Message}");
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
            // CA1031: the batch-level net must never abort the app; unexpected failures are logged
            // and surfaced to the user as an error entry.
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.Error(ex, "Re-archive all failed unexpectedly");
                lock (errorsLock)
                    errors.Add($"Re-archive failed unexpectedly: {ex.Message}");
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
                NamingMask.DefaultMask,
                ArchiveFormat
            );

            if (target is null)
                return;

            GameRowVM snapshotGame = SelectedGame;
            ProgressWindowVM progressVm = new ProgressWindowVM(1, isCancellable: true);
            Task<string?> operationTask = TrimSelectedCoreAsync(
                snapshotGame,
                target.Value,
                progressVm
            );
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
            try
            {
                progress.CurrentFile = Path.GetFileName(target.From);
                IProgress<int> compressionProgress = new Progress<int>(pct =>
                    progress.Progress = pct
                );
                Result<MatchResult> result = await _trimService.TrimAsync(
                    game.Result,
                    target,
                    ArchiveFormat,
                    progress.CancellationToken,
                    compressionProgress
                );

                if (result.IsFailed)
                    return result.Errors[0].Message;

                await ReplaceGameAsync(game, result.Value);
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

            await _batchRunner.RunAsync(
                new BatchProgressOperation<GameRowVM>
                {
                    Title = "Trimming ROMs",
                    LogLabel = "Trim all",
                    FailureLabel = "Trim",
                    CompletedVerb = "succeeded",
                    Targets = targets,
                    FileName = g => g.ScannedRom?.FilePath ?? string.Empty,
                    ProcessAsync = TrimOneAsync,
                    IsCancellable = true,
                    // TrimOneAsync publishes fractional overall progress across items itself.
                    BumpOverallProgress = false,
                    BusyFlag = busy => IsTrimming = busy,
                }
            );
        }

        private async Task<string?> TrimOneAsync(GameRowVM game, ProgressWindowVM progress)
        {
            (string From, string To)? target = RomTrimmer.GetTrimTarget(
                game.Result,
                NamingMask.DefaultMask,
                ArchiveFormat
            );

            if (target is null)
                return null;

            // Map this file's compression progress into its slice of the overall bar; the batch
            // runner leaves BumpOverallProgress off for trim so each file publishes its own fraction.
            int fileBase = (progress.Current - 1) * 100 / progress.Total;
            int fileRange = 100 / progress.Total;
            IProgress<int> compressionProgress = new Progress<int>(pct =>
                progress.Progress = fileBase + pct * fileRange / 100
            );

            Result<MatchResult> result = await _trimService.TrimAsync(
                game.Result,
                target.Value,
                ArchiveFormat,
                progress.CancellationToken,
                compressionProgress
            );

            if (result.IsFailed)
                return result.Errors[0].Message;

            await ReplaceGameAsync(game, result.Value);
            return null;
        }

        private bool CanTrimAll() =>
            !IsTrimming
            && !IsReArchiving
            && _compressor.IsAvailable
            && ActiveDat is not null
            && ActiveDat.Games.Any(g => g.IsUntrimmed);

        private async Task ReplaceGameAsync(GameRowVM original, MatchResult updatedMatch)
        {
            var index = ActiveDat!.Games.IndexOf(original);
            if (index < 0)
                return;
            var updatedRow = ActiveDat.BuildGameRow(updatedMatch);
            ActiveDat.Games[index] = updatedRow;
            if (ReferenceEquals(SelectedGame, original))
                SelectedGame = updatedRow;
            original.Dispose();
            await _scanResultStore.UpdateResultAsync(
                ActiveDat.DatFile.Header.DatName,
                updatedMatch
            );
        }

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

            LoadedDatVM activeDat = ActiveDat;
            List<ScannedRom> targets = activeDat.UnmatchedRoms.ToList();
            List<ScannedRom> moved = new List<ScannedRom>();

            await _batchRunner.RunAsync(
                new BatchProgressOperation<ScannedRom>
                {
                    Title = "Moving Unverified Files",
                    LogLabel = "Move unverified",
                    FailureLabel = "Move",
                    CompletedVerb = "moved",
                    Targets = targets,
                    FileName = rom => rom.FilePath,
                    ProcessAsync = (rom, _) => MoveOneAsync(rom, destFolder, moved),
                    IsCancellable = true,
                    BumpOverallProgress = true,
                }
            );

            if (moved.Count > 0)
                activeDat.UnmatchedRoms = activeDat
                    .UnmatchedRoms.Where(r => !moved.Contains(r))
                    .ToList();
        }

        private async Task<string?> MoveOneAsync(
            ScannedRom rom,
            string destFolder,
            List<ScannedRom> moved
        )
        {
            string destPath = Path.Combine(destFolder, Path.GetFileName(rom.FilePath));
            Result result = await _fileOperations.RenameAsync(rom.FilePath, destPath);

            if (result.IsFailed)
                return $"{Path.GetFileName(rom.FilePath)}: {result.Errors[0].Message}";

            moved.Add(rom);
            return null;
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

            Result<DatUpdateCheck> checkResult = await _datUpdateService.CheckForUpdateAsync(
                header
            );
            if (checkResult.IsFailed)
            {
                await _notifier.NotifyErrorAsync(
                    $"Could not check for updates.\n{checkResult.Errors[0].Message}"
                );
                return;
            }

            DatUpdateCheck check = checkResult.Value;
            if (!check.IsNewer)
            {
                await _notifier.NotifyInfoAsync(
                    $"Already up to date (version {check.CurrentVersion})."
                );
                return;
            }

            var confirmed = await _notifier.ConfirmAsync(
                "Update Available",
                $"A newer DAT version is available (current: {check.CurrentVersion}, latest: {check.LatestVersion}).\n\nDownload the update now?"
            );
            if (!confirmed)
                return;

            var progressVm = new ProgressWindowVM(0, isCancellable: true);
            progressVm.CurrentFile = "Downloading DAT…";

            IProgress<int> datProgress = new Progress<int>(p => progressVm.Progress = p);
            Task<Result> updateTask = _datUpdateService.DownloadUpdateAsync(
                header,
                datProgress,
                progressVm.CancellationToken
            );
            await _notifier.ShowProgressAsync(
                $"Updating DAT — {ActiveDat.DisplayTitle}",
                progressVm,
                updateTask
            );

            var updateResult = await updateTask;
            if (updateResult.IsFailed)
            {
                await _notifier.NotifyErrorAsync(
                    $"Update failed.\n{updateResult.Errors[0].Message}"
                );
                return;
            }

            await LoadDatFromManagedPathAsync(ActiveDat.DatFilePath);

            if (ActiveDat?.DatFile.Header.NewImUrl is not null)
                await RunImageDownloadUiAsync(ActiveDat.DisplayTitle, ActiveDat.DatFile);
        }

        private bool CanCheckDatUpdate() =>
            ActiveDat is not null && ActiveDat.DatFile.Header.NewDatVersionUrl is not null;

        [RelayCommand(CanExecute = nameof(CanDownloadImages))]
        private async Task DownloadImagesAsync()
        {
            if (ActiveDat?.DatFile.Header.NewImUrl is null)
                return;

            await RunImageDownloadUiAsync(ActiveDat.DisplayTitle, ActiveDat.DatFile);
        }

        private bool CanDownloadImages() =>
            ActiveDat is not null && ActiveDat.DatFile.Header.NewImUrl is not null;

        /// <summary>
        /// Opens the image-download window and runs a missing-image sync for the given DAT. Only images
        /// absent from disk are fetched.
        /// </summary>
        private async Task RunImageDownloadUiAsync(string datDisplayName, DatFile datFile)
        {
            using ImageDownloadWindowVM imageVm = new ImageDownloadWindowVM();
            // CA2025: ShowImageDownloadAsync keeps the modal dialog open until the sync completes
            // (window.Closing blocks while !vm.IsComplete), so imageVm is never disposed by this
            // `using` while syncTask is still running.
#pragma warning disable CA2025
            Task syncTask = RunImageSyncAsync(datFile, imageVm);
            await _notifier.ShowImageDownloadAsync(
                $"Downloading Images — {datDisplayName}",
                imageVm,
                syncTask
            );
#pragma warning restore CA2025
        }

        private async Task RunImageSyncAsync(DatFile datFile, ImageDownloadWindowVM imageVm)
        {
            IProgress<ImageSyncProgress> progress = new Progress<ImageSyncProgress>(imageVm.Report);
            try
            {
                Result<ImageSyncSummary> result = await _imageSync.SyncMissingAsync(
                    datFile,
                    _appData.ImgsPath,
                    progress,
                    imageVm.CancellationToken
                );

                imageVm.Finish(result.IsSuccess ? result.Value : new ImageSyncSummary(0, 0, 0));
            }
            catch (OperationCanceledException)
            {
                imageVm.Cancelled();
            }
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
        private async Task CheckForUpdatesAsync() =>
            await RunUpdateCheckAsync(announceWhenCurrent: true);

        /// <summary>
        /// Checks for a newer release. When <paramref name="announceWhenCurrent"/> is <c>false</c>
        /// (the startup check), an up-to-date result or a failed check stays silent; only an available
        /// update prompts the user.
        /// </summary>
        private async Task RunUpdateCheckAsync(bool announceWhenCurrent)
        {
            try
            {
                UpdateCheckOutcome outcome = await _updateCheck.CheckAsync();

                if (outcome.Status == UpdateCheckStatus.UpdateAvailable)
                {
                    bool open = await _notifier.ConfirmAsync(
                        "Update Available",
                        $"RomForge {outcome.LatestVersion} is available — you have {outcome.CurrentVersion}.\n\nOpen the download page?"
                    );
                    if (open && outcome.ReleaseUrl is not null)
                        await _urlLauncher.OpenUrlAsync(outcome.ReleaseUrl);
                }
                else if (announceWhenCurrent && outcome.Status == UpdateCheckStatus.UpToDate)
                {
                    await _notifier.NotifyInfoAsync(
                        $"You're on the latest version ({outcome.CurrentVersion})."
                    );
                }
                else if (announceWhenCurrent && outcome.Status == UpdateCheckStatus.CheckFailed)
                {
                    await _notifier.NotifyErrorAsync(
                        $"Could not check for updates.\n{outcome.Error}"
                    );
                }
            }
            // CA1031: an uncaught throw here (e.g. from launching the release page) would abort the
            // app via the RelayCommand. The startup check stays silent; only the interactive path
            // surfaces the failure.
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.Error(ex, "Update check failed unexpectedly");
                if (announceWhenCurrent)
                    await _notifier.NotifyErrorAsync($"Could not check for updates.\n{ex.Message}");
            }
        }

        /// <summary>
        /// The running application's version, e.g. "1.2.0".
        /// </summary>
        public string AppVersion => _updateCheck.CurrentVersion;

        /// <summary>
        /// Version label for the status bar, e.g. "RomForge v1.2.0".
        /// </summary>
        public string AppVersionDisplay => $"RomForge v{_updateCheck.CurrentVersion}";

        [RelayCommand]
        private async Task OpenReleasesPageAsync() =>
            await _urlLauncher.OpenUrlAsync(GitHubReleaseChecker.ReleasesPageUrl);

        [RelayCommand]
        private async Task ShowAboutAsync()
        {
            string message =
                $"RomForge v{_updateCheck.CurrentVersion}\n"
                + "A ROM collection manager: scan, verify against DATs, rename, trim and re-archive.\n\n"
                + "© 2026 Ben de Bruijn\n"
                + GitHubReleaseChecker.ReleasesPageUrl;

            bool openReleases = await _notifier.ShowAboutAsync(message);
            if (openReleases)
                await _urlLauncher.OpenUrlAsync(GitHubReleaseChecker.ReleasesPageUrl);
        }

        [RelayCommand]
        private void Quit() => _appLifetime.Shutdown();
    }
}
