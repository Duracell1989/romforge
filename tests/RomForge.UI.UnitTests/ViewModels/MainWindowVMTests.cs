using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Input;
using FluentResults;
using Moq;
using NUnit.Framework;
using RomForge.Core.IO;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Scanning;
using RomForge.Core.Services;
using RomForge.UI.Services;
using RomForge.UI.ViewModels;
using Serilog;

namespace RomForge.UI.UnitTests.ViewModels;

[TestOf(typeof(MainWindowVM))]
public sealed class MainWindowVMTests
{
    private string _tempDir = null!;
    private MainWindowVM _vm = null!;
    private Mock<IFileDialogService> _fileDialogs = null!;
    private Mock<IDatReader> _datReader = null!;
    private Mock<IUserNotifier> _notifier = null!;
    private Mock<IDatImporter> _datImporter = null!;
    private Mock<IRomFileOperations> _fileOps = null!;
    private Mock<IArchiveCompressor> _compressor = null!;
    private Mock<IArchiveExtractor> _extractor = null!;
    private Mock<IDatUpdateChecker> _updateChecker = null!;
    private Mock<IDatDownloader> _downloader = null!;
    private Mock<IImageDownloader> _imageDownloader = null!;
    private Mock<IAppLifetime> _appLifetime = null!;
    private Mock<IRomSource> _romSource = null!;
    private Mock<IReleaseChecker> _releaseChecker = null!;
    private Mock<IUrlLauncher> _urlLauncher = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _fileDialogs = new Mock<IFileDialogService>();
        _datReader = new Mock<IDatReader>();
        _notifier = new Mock<IUserNotifier>();
        _datImporter = new Mock<IDatImporter>();
        _fileOps = new Mock<IRomFileOperations>();
        _compressor = new Mock<IArchiveCompressor>();
        _extractor = new Mock<IArchiveExtractor>();
        _updateChecker = new Mock<IDatUpdateChecker>();
        _downloader = new Mock<IDatDownloader>();
        _imageDownloader = new Mock<IImageDownloader>();
        _appLifetime = new Mock<IAppLifetime>();
        _romSource = new Mock<IRomSource>();
        _romSource
            .Setup(s => s.EnumerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyRomSourceAsync());
        _releaseChecker = new Mock<IReleaseChecker>();
        // Default: the startup update check silently fails (no network), so it never notifies.
        _releaseChecker
            .Setup(c => c.FetchLatestReleaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<ReleaseInfo>("no network"));
        _urlLauncher = new Mock<IUrlLauncher>();
        _vm = MakeVM();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private MainWindowVM MakeVM(Mock<IArchiveCompressor>? compressorMock = null)
    {
        ILogger logger = new LoggerConfiguration().CreateLogger();
        AppDataService appData = new AppDataService(_tempDir);

        return new MainWindowVM(
            _fileDialogs.Object,
            _ => _datReader.Object,
            _romSource.Object,
            _fileOps.Object,
            compressorMock?.Object ?? _compressor.Object,
            _extractor.Object,
            _notifier.Object,
            _urlLauncher.Object,
            new UpdateCheckService(_releaseChecker.Object, logger, "1.0.0"),
            logger,
            appData,
            _datImporter.Object,
            _updateChecker.Object,
            _downloader.Object,
            new ImageSyncService(_imageDownloader.Object, _fileOps.Object, logger),
            new DatConfigService(appData, logger),
            new ScanResultStore(appData, logger),
            new ReArchiveStore(appData, logger),
            new AppPreferencesService(appData, logger),
            MakeInlineDispatcher(),
            _appLifetime.Object
        );
    }

    private static async IAsyncEnumerable<RomContent> EmptyRomSourceAsync()
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>
    /// Dispatcher stub that runs the callback inline so UI-thread update logic executes under test.
    /// </summary>
    private static IUiDispatcher MakeInlineDispatcher()
    {
        Mock<IUiDispatcher> dispatcher = new Mock<IUiDispatcher>();
        dispatcher
            .Setup(d => d.InvokeAsync(It.IsAny<Action>()))
            .Returns<Action>(action =>
            {
                action();
                return Task.CompletedTask;
            });
        return dispatcher.Object;
    }

    private static LoadedDatVM MakeDatVM(string name = "Test DAT") =>
        new LoadedDatVM(
            new DatFile
            {
                Header = new DatHeader { DatName = name },
                Games = [],
            },
            "/test/dat.xml"
        );

    private static LoadedDatVM MakeDatVMWithUpdateUrl() =>
        new LoadedDatVM(
            new DatFile
            {
                Header = new DatHeader
                {
                    DatName = "Test DAT",
                    NewDatVersionUrl = "https://example.com/version.txt",
                    NewDatUrl = "https://example.com/dat.zip",
                    DatVersion = 0,
                },
                Games = [],
            },
            "/test/dat.xml"
        );

    private static LoadedDatVM MakeDatVMWithImageUrl() =>
        new LoadedDatVM(
            new DatFile
            {
                Header = new DatHeader
                {
                    DatName = "Test DAT",
                    NewImUrl = "https://example.com/imgs/",
                },
                Games = [],
            },
            "/test/dat.xml"
        );

    private static GameRowVM MakeGameRow(
        bool incorrectlyNamed = false,
        bool wrongArchiveType = false,
        bool untrimmed = false
    ) =>
        new GameRowVM(
            new MatchResult
            {
                Game = new Game { Title = "Test Game" },
                Status = MatchStatus.Verified,
                IsIncorrectlyNamed = incorrectlyNamed,
                IsWrongArchiveType = wrongArchiveType,
                IsUntrimmed = untrimmed,
            },
            string.Empty,
            new DatHeader(),
            []
        );

    // --- Construction defaults ---

    [Test]
    public void ArchiveFormat_OnConstruction_Is7z() => _vm.ArchiveFormat.Should().Be("7z");

    [Test]
    public void LoadedDats_OnConstruction_IsEmpty() => _vm.LoadedDats.Should().BeEmpty();

    [Test]
    public void IsDatLoaded_OnConstruction_IsFalse() => _vm.IsDatLoaded.Should().BeFalse();

    [Test]
    public void StatusSummary_OnConstruction_IsNoDatLoaded() =>
        _vm.StatusSummary.Should().Be("No DAT loaded");

    [Test]
    public void MoveUnverifiedLabel_OnConstruction_ShowsZeroCount() =>
        _vm.MoveUnverifiedLabel.Should().Be("Move Unverified (0)");

    // --- Computed label properties ---

    [Test]
    public void ReArchiveButtonLabel_DefaultsTo7z() =>
        _vm.ReArchiveButtonLabel.Should().Be("Re-Archive to 7z");

    [Test]
    public void ReArchiveAllButtonLabel_DefaultsTo7z() =>
        _vm.ReArchiveAllButtonLabel.Should().Be("Re-Archive All to 7z");

    [Test]
    public void ReArchiveButtonLabel_ReflectsFormatChange()
    {
        _vm.ArchiveFormat = "zip";

        _vm.ReArchiveButtonLabel.Should().Be("Re-Archive to zip");
    }

    [Test]
    public void ReArchiveAllButtonLabel_ReflectsFormatChange()
    {
        _vm.ArchiveFormat = "zip";

        _vm.ReArchiveAllButtonLabel.Should().Be("Re-Archive All to zip");
    }

    [Test]
    public void ReArchiveButtonLabel_RaisesPropertyChanged_WhenArchiveFormatChanges()
    {
        List<string?> raised = [];
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _vm.ArchiveFormat = "zip";

        raised.Should().Contain(nameof(MainWindowVM.ReArchiveButtonLabel));
    }

    [Test]
    public void ReArchiveAllButtonLabel_RaisesPropertyChanged_WhenArchiveFormatChanges()
    {
        List<string?> raised = [];
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _vm.ArchiveFormat = "zip";

        raised.Should().Contain(nameof(MainWindowVM.ReArchiveAllButtonLabel));
    }

    // --- ActiveDat effects ---

    [Test]
    public void IsDatLoaded_WhenActiveDatIsSet_IsTrue()
    {
        _vm.ActiveDat = MakeDatVM();

        _vm.IsDatLoaded.Should().BeTrue();
    }

    [Test]
    public void IsDatLoaded_WhenActiveDatIsCleared_IsFalse()
    {
        _vm.ActiveDat = MakeDatVM();
        _vm.ActiveDat = null;

        _vm.IsDatLoaded.Should().BeFalse();
    }

    [Test]
    public void IsDatLoaded_RaisesPropertyChanged_WhenActiveDatChanges()
    {
        List<string?> raised = [];
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _vm.ActiveDat = MakeDatVM();

        raised.Should().Contain(nameof(MainWindowVM.IsDatLoaded));
    }

    [Test]
    public void StatusSummary_WhenActiveDatIsSet_IsNotNoDatLoaded()
    {
        _vm.ActiveDat = MakeDatVM();

        _vm.StatusSummary.Should().NotBe("No DAT loaded");
    }

    [Test]
    public void StatusSummary_WhenActiveDatIsCleared_IsNoDatLoaded()
    {
        _vm.ActiveDat = MakeDatVM();
        _vm.ActiveDat = null;

        _vm.StatusSummary.Should().Be("No DAT loaded");
    }

    [Test]
    public void OnActiveDatChanged_ResetsSelectedGame()
    {
        _vm.SelectedGame = MakeGameRow();

        _vm.ActiveDat = MakeDatVM();

        _vm.SelectedGame.Should().BeNull();
    }

    [Test]
    public void OnActiveDatChanged_WhenCleared_LeavesArchiveFormatUnchanged()
    {
        _vm.ArchiveFormat = "zip";
        _vm.ActiveDat = MakeDatVM();

        _vm.ActiveDat = null;

        _vm.ArchiveFormat.Should().Be("zip");
    }

    [Test]
    public void MoveUnverifiedLabel_RaisesPropertyChanged_WhenActiveDatChanges()
    {
        List<string?> raised = [];
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _vm.ActiveDat = MakeDatVM();

        raised.Should().Contain(nameof(MainWindowVM.MoveUnverifiedLabel));
    }

    [Test]
    public void StatusSummary_TracksActiveDatStatusSummaryChanges()
    {
        LoadedDatVM dat = MakeDatVM();
        _vm.ActiveDat = dat;

        List<string?> raised = [];
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Mutating Games triggers StatusSummary recalculation on LoadedDatVM
        dat.Games = new ObservableCollection<GameRowVM> { MakeGameRow() };

        raised.Should().Contain(nameof(MainWindowVM.StatusSummary));
    }

    // --- CanExecute gates ---

    [Test]
    public void ScanFolderCommand_CannotExecute_WhenNoDatLoaded()
    {
        _vm.ActiveDat = null;

        _vm.ScanFolderCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void ScanFolderCommand_CanExecute_WhenDatIsLoaded()
    {
        _vm.ActiveDat = MakeDatVM();

        _vm.ScanFolderCommand.CanExecute(null).Should().BeTrue();
    }

    [Test]
    public void RemoveDatCommand_CannotExecute_WhenNoDatLoaded()
    {
        _vm.ActiveDat = null;

        _vm.RemoveDatCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void RemoveDatCommand_CanExecute_WhenDatIsLoaded()
    {
        _vm.ActiveDat = MakeDatVM();

        _vm.RemoveDatCommand.CanExecute(null).Should().BeTrue();
    }

    [Test]
    public void RenameSelectedCommand_CannotExecute_WhenNoGameSelected()
    {
        _vm.SelectedGame = null;

        _vm.RenameSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void RenameSelectedCommand_CannotExecute_WhenSelectedGameIsCorrectlyNamed()
    {
        _vm.SelectedGame = MakeGameRow(incorrectlyNamed: false);

        _vm.RenameSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void RenameSelectedCommand_CanExecute_WhenSelectedGameIsIncorrectlyNamed()
    {
        _vm.SelectedGame = MakeGameRow(incorrectlyNamed: true);

        _vm.RenameSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    [Test]
    public void TrimSelectedCommand_CannotExecute_WhenNoGameSelected()
    {
        _vm.SelectedGame = null;

        _vm.TrimSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void TrimSelectedCommand_CannotExecute_WhenCompressorUnavailable()
    {
        // _compressor.IsAvailable returns false by default
        _vm.SelectedGame = MakeGameRow(untrimmed: true);

        _vm.TrimSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void TrimSelectedCommand_CanExecute_WhenGameIsUntrimmedAndCompressorAvailable()
    {
        _compressor.Setup(c => c.IsAvailable).Returns(true);
        _vm.SelectedGame = MakeGameRow(untrimmed: true);

        _vm.TrimSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    [Test]
    public void ReArchiveSelectedCommand_CannotExecute_WhenNoGameSelected()
    {
        _vm.SelectedGame = null;

        _vm.ReArchiveSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void ReArchiveSelectedCommand_CannotExecute_WhenCompressorUnavailable()
    {
        Mock<IArchiveCompressor> compressor = new Mock<IArchiveCompressor>();
        compressor.Setup(c => c.IsAvailable).Returns(false);
        MainWindowVM vm = MakeVM(compressor);
        vm.SelectedGame = MakeGameRow(wrongArchiveType: true);

        vm.ReArchiveSelectedCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void ReArchiveSelectedCommand_CanExecute_WhenGameIsWrongArchiveTypeAndCompressorAvailable()
    {
        _compressor.Setup(c => c.IsAvailable).Returns(true);
        _vm.SelectedGame = MakeGameRow(wrongArchiveType: true);

        _vm.ReArchiveSelectedCommand.CanExecute(null).Should().BeTrue();
    }

    [Test]
    public void RenameAllCommand_CannotExecute_WhenNoDat()
    {
        _vm.ActiveDat = null;

        _vm.RenameAllCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void RenameAllCommand_CannotExecute_WhenNoIncorrectlyNamedGames()
    {
        _vm.ActiveDat = MakeDatVM();

        _vm.RenameAllCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void RenameAllCommand_CanExecute_WhenDatHasIncorrectlyNamedGames()
    {
        LoadedDatVM dat = MakeDatVM();
        dat.Games.Add(MakeGameRow(incorrectlyNamed: true));
        _vm.ActiveDat = dat;

        _vm.RenameAllCommand.CanExecute(null).Should().BeTrue();
    }

    [Test]
    public void ReArchiveAllCommand_CannotExecute_WhenCompressorUnavailable()
    {
        // _compressor.IsAvailable returns false by default
        LoadedDatVM dat = MakeDatVM();
        dat.Games.Add(MakeGameRow(wrongArchiveType: true));
        _vm.ActiveDat = dat;

        _vm.ReArchiveAllCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void ReArchiveAllCommand_CanExecute_WhenDatHasReArchivableGamesAndCompressorAvailable()
    {
        _compressor.Setup(c => c.IsAvailable).Returns(true);
        LoadedDatVM dat = MakeDatVM();
        dat.Games.Add(MakeGameRow(wrongArchiveType: true));
        _vm.ActiveDat = dat;

        _vm.ReArchiveAllCommand.CanExecute(null).Should().BeTrue();
    }

    [Test]
    public async Task ReArchiveAllAsync_WhenOperationThrowsUnexpectedly_DoesNotCrashAndNotifiesError()
    {
        GameRowVM game = new GameRowVM(
            new MatchResult
            {
                Game = new Game { Title = "Test Game" },
                Status = MatchStatus.Verified,
                IsWrongArchiveType = true,
                ScannedRom = new ScannedRom { FilePath = "/roms/game.zip" },
            },
            string.Empty,
            new DatHeader(),
            []
        );
        LoadedDatVM dat = MakeDatVM();
        dat.Games = new ObservableCollection<GameRowVM> { game };
        _vm.ActiveDat = dat;
        _compressor.Setup(c => c.IsAvailable).Returns(true);
        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        Func<Task> act = async () => await _vm.ReArchiveAllCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public void MoveUnverifiedCommand_CannotExecute_WhenNoUnmatchedRoms()
    {
        _vm.ActiveDat = MakeDatVM();

        _vm.MoveUnverifiedCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void MoveUnverifiedCommand_CanExecute_WhenUnmatchedRomsExist()
    {
        LoadedDatVM dat = MakeDatVM();
        dat.UnmatchedRoms = [new ScannedRom { FilePath = "/roms/unknown.zip" }];
        _vm.ActiveDat = dat;

        _vm.MoveUnverifiedCommand.CanExecute(null).Should().BeTrue();
    }

    [Test]
    public void CheckDatUpdateCommand_CannotExecute_WhenDatHasNoUpdateUrl()
    {
        _vm.ActiveDat = MakeDatVM();

        _vm.CheckDatUpdateCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void CheckDatUpdateCommand_CanExecute_WhenDatHasUpdateUrl()
    {
        _vm.ActiveDat = MakeDatVMWithUpdateUrl();

        _vm.CheckDatUpdateCommand.CanExecute(null).Should().BeTrue();
    }

    // --- DownloadImagesAsync ---

    [Test]
    public void DownloadImagesCommand_CannotExecute_WhenNoDatLoaded()
    {
        _vm.ActiveDat = null;

        _vm.DownloadImagesCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void DownloadImagesCommand_CannotExecute_WhenDatHasNoImageUrl()
    {
        _vm.ActiveDat = MakeDatVM();

        _vm.DownloadImagesCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void DownloadImagesCommand_CanExecute_WhenDatHasImageUrl()
    {
        _vm.ActiveDat = MakeDatVMWithImageUrl();

        _vm.DownloadImagesCommand.CanExecute(null).Should().BeTrue();
    }

    [Test]
    public async Task DownloadImages_WhenDatHasImageUrl_ShowsImageDownloadWindow()
    {
        _vm.ActiveDat = MakeDatVMWithImageUrl();
        _notifier
            .Setup(n =>
                n.ShowImageDownloadAsync(It.IsAny<ImageDownloadWindowVM>(), It.IsAny<Task>())
            )
            .Returns<ImageDownloadWindowVM, Task>((_, task) => task);

        await _vm.DownloadImagesCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.ShowImageDownloadAsync(It.IsAny<ImageDownloadWindowVM>(), It.IsAny<Task>()),
            Times.Once
        );
    }

    [Test]
    public async Task DownloadImages_WhenDatHasNoImageUrl_DoesNotShowWindow()
    {
        _vm.ActiveDat = MakeDatVM(); // no image URL

        await _vm.DownloadImagesCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.ShowImageDownloadAsync(It.IsAny<ImageDownloadWindowVM>(), It.IsAny<Task>()),
            Times.Never
        );
    }

    // --- RemoveDat ---

    [Test]
    public void RemoveDat_WhenOnlyDat_SetsActiveDatToNull()
    {
        LoadedDatVM dat = MakeDatVM();
        _vm.LoadedDats.Add(dat);
        _vm.ActiveDat = dat;

        _vm.RemoveDatCommand.Execute(null);

        _vm.ActiveDat.Should().BeNull();
        _vm.LoadedDats.Should().BeEmpty();
    }

    [Test]
    public void RemoveDat_WhenFirstOfTwo_SelectsRemainingDat()
    {
        LoadedDatVM dat1 = MakeDatVM("DAT 1");
        LoadedDatVM dat2 = MakeDatVM("DAT 2");
        _vm.LoadedDats.Add(dat1);
        _vm.LoadedDats.Add(dat2);
        _vm.ActiveDat = dat1;

        _vm.RemoveDatCommand.Execute(null);

        _vm.ActiveDat.Should().Be(dat2);
        _vm.LoadedDats.Should().HaveCount(1);
    }

    [Test]
    public void RemoveDat_WhenSecondOfTwo_SelectsFirstDat()
    {
        LoadedDatVM dat1 = MakeDatVM("DAT 1");
        LoadedDatVM dat2 = MakeDatVM("DAT 2");
        _vm.LoadedDats.Add(dat1);
        _vm.LoadedDats.Add(dat2);
        _vm.ActiveDat = dat2;

        _vm.RemoveDatCommand.Execute(null);

        _vm.ActiveDat.Should().Be(dat1);
        _vm.LoadedDats.Should().HaveCount(1);
    }

    // --- Quit ---

    [Test]
    public void Quit_CallsAppLifetimeShutdown()
    {
        _vm.QuitCommand.Execute(null);

        _appLifetime.Verify(a => a.Shutdown(), Times.Once);
    }

    // --- CheckForUpdatesAsync ---

    private void SetupLatestRelease(string tag) =>
        _releaseChecker
            .Setup(c => c.FetchLatestReleaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Result.Ok(new ReleaseInfo(tag, $"https://example.com/releases/tag/{tag}"))
            );

    [Test]
    public async Task CheckForUpdates_WhenNewerReleaseAndConfirmed_OpensReleasePage()
    {
        // Current version is "1.0.0" (see MakeVM)
        SetupLatestRelease("v2.0.0");
        _notifier
            .Setup(n => n.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        await _vm.CheckForUpdatesCommand.ExecuteAsync(null);

        _urlLauncher.Verify(
            l => l.OpenUrlAsync("https://example.com/releases/tag/v2.0.0"),
            Times.Once
        );
    }

    [Test]
    public async Task CheckForUpdates_WhenNewerReleaseButDeclined_DoesNotOpenPage()
    {
        SetupLatestRelease("v2.0.0");
        _notifier
            .Setup(n => n.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        await _vm.CheckForUpdatesCommand.ExecuteAsync(null);

        _urlLauncher.Verify(l => l.OpenUrlAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task CheckForUpdates_WhenUpToDate_NotifiesInfo()
    {
        SetupLatestRelease("v1.0.0"); // same as current

        await _vm.CheckForUpdatesCommand.ExecuteAsync(null);

        _notifier.Verify(n => n.NotifyInfoAsync(It.IsAny<string>()), Times.Once);
        _urlLauncher.Verify(l => l.OpenUrlAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task CheckForUpdates_WhenCheckFails_NotifiesError()
    {
        // Default mock returns a failed result.
        await _vm.CheckForUpdatesCommand.ExecuteAsync(null);

        _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Once);
    }

    // --- ImportDatAsync ---

    [Test]
    public async Task ImportDatAsync_WhenNoFileSelected_DoesNotNotifyError()
    {
        _fileDialogs.Setup(d => d.PickDatFileAsync()).ReturnsAsync((string?)null);

        await _vm.ImportDatCommand.ExecuteAsync(null);

        _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ImportDatAsync_WhenReadFails_NotifiesError()
    {
        _fileDialogs.Setup(d => d.PickDatFileAsync()).ReturnsAsync("/fake/file.dat");
        _datReader.Setup(r => r.ReadAsync()).ReturnsAsync(Result.Fail<DatFile>("read error"));

        await _vm.ImportDatCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("read error"))),
            Times.Once
        );
    }

    [Test]
    public async Task ImportDatAsync_WhenImportFails_NotifiesError()
    {
        _fileDialogs.Setup(d => d.PickDatFileAsync()).ReturnsAsync("/fake/file.dat");
        _datReader
            .Setup(r => r.ReadAsync())
            .ReturnsAsync(
                Result.Ok(
                    new DatFile
                    {
                        Header = new DatHeader { DatName = "Test" },
                        Games = [],
                    }
                )
            );
        _datImporter
            .Setup(i =>
                i.ImportAsync(
                    It.IsAny<string>(),
                    It.IsAny<DatHeader>(),
                    It.IsAny<IProgress<ImportProgress>?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Fail<string>("import error"));

        await _vm.ImportDatCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("import error"))),
            Times.Once
        );
    }

    // --- ScanFolderAsync ---

    [Test]
    public async Task ScanFolderAsync_WhenNoFolderSelected_DoesNotSetRomFolder()
    {
        _vm.ActiveDat = MakeDatVM();
        _fileDialogs.Setup(d => d.PickRomFolderAsync()).ReturnsAsync((string?)null);

        await _vm.ScanFolderCommand.ExecuteAsync(null);

        _vm.ActiveDat!.RomFolder.Should().BeNull();
    }

    // --- LoadManagedDatsAsync ---

    [Test]
    public async Task LoadManagedDatsAsync_WhenNoDatsImported_LoadedDatsRemainsEmpty()
    {
        await _vm.LoadManagedDatsAsync();

        _vm.LoadedDats.Should().BeEmpty();
        _vm.ActiveDat.Should().BeNull();
    }

    private async Task PersistUnverifiedFolderAsync(string? folder) =>
        await new AppPreferencesService(
            new AppDataService(_tempDir),
            new LoggerConfiguration().CreateLogger()
        ).UpdateSettingsAsync("7z", folder);

    [Test]
    public async Task LoadManagedDatsAsync_WhenSavedUnverifiedFolderMissing_NotifiesAndOpensSettings()
    {
        await PersistUnverifiedFolderAsync("/does/not/exist");
        _fileOps.Setup(f => f.DirectoryExists("/does/not/exist")).Returns(false);

        await _vm.LoadManagedDatsAsync();

        _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Once);
        _notifier.Verify(n => n.ShowSettingsAsync(It.IsAny<SettingsVM>()), Times.Once);
    }

    [Test]
    public async Task LoadManagedDatsAsync_WhenSavedUnverifiedFolderExists_DoesNotOpenSettings()
    {
        await PersistUnverifiedFolderAsync("/exists");
        _fileOps.Setup(f => f.DirectoryExists("/exists")).Returns(true);

        await _vm.LoadManagedDatsAsync();

        _notifier.Verify(n => n.ShowSettingsAsync(It.IsAny<SettingsVM>()), Times.Never);
    }

    [Test]
    public async Task LoadManagedDatsAsync_WhenNoUnverifiedFolderSaved_DoesNotOpenSettings()
    {
        await _vm.LoadManagedDatsAsync();

        _notifier.Verify(n => n.ShowSettingsAsync(It.IsAny<SettingsVM>()), Times.Never);
    }

    [Test]
    public async Task LoadManagedDatsAsync_WhenSavedFolderMissingAndUserDoesNotFixIt_DropsFolderSoMoveFallsBackToPicker()
    {
        await PersistUnverifiedFolderAsync("/does/not/exist");
        _fileOps.Setup(f => f.DirectoryExists("/does/not/exist")).Returns(false);
        // Settings dialog is a no-op (user cancels without choosing a new folder).
        await _vm.LoadManagedDatsAsync();

        LoadedDatVM dat = MakeDatVM();
        dat.UnmatchedRoms = [new ScannedRom { FilePath = "/roms/unknown.zip" }];
        _vm.ActiveDat = dat;
        _fileDialogs.Setup(d => d.PickUnverifiedDestinationAsync()).ReturnsAsync((string?)null);

        await _vm.MoveUnverifiedCommand.ExecuteAsync(null);

        _fileDialogs.Verify(d => d.PickUnverifiedDestinationAsync(), Times.Once);
    }

    // --- MoveUnverifiedAsync ---

    [Test]
    public async Task MoveUnverifiedAsync_WhenNoDestinationSelected_DoesNotCallFileOps()
    {
        LoadedDatVM dat = MakeDatVM();
        dat.UnmatchedRoms = [new ScannedRom { FilePath = "/roms/unknown.zip" }];
        _vm.ActiveDat = dat;
        _fileDialogs.Setup(d => d.PickUnverifiedDestinationAsync()).ReturnsAsync((string?)null);

        await _vm.MoveUnverifiedCommand.ExecuteAsync(null);

        _fileOps.Verify(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task MoveUnverifiedAsync_WhenMoveSucceeds_ClearsUnmatchedRoms()
    {
        LoadedDatVM dat = MakeDatVM();
        dat.UnmatchedRoms = [new ScannedRom { FilePath = "/roms/unknown.zip" }];
        _vm.ActiveDat = dat;
        _fileDialogs.Setup(d => d.PickUnverifiedDestinationAsync()).ReturnsAsync("/dest");
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());

        await _vm.MoveUnverifiedCommand.ExecuteAsync(null);

        dat.UnmatchedRoms.Should().BeEmpty();
    }

    [Test]
    public async Task MoveUnverifiedAsync_WhenMoveFails_NotifiesError()
    {
        LoadedDatVM dat = MakeDatVM();
        dat.UnmatchedRoms = [new ScannedRom { FilePath = "/roms/unknown.zip" }];
        _vm.ActiveDat = dat;
        _fileDialogs.Setup(d => d.PickUnverifiedDestinationAsync()).ReturnsAsync("/dest");
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Fail("permission denied"));

        await _vm.MoveUnverifiedCommand.ExecuteAsync(null);

        _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Once);
    }

    // --- CheckDatUpdateAsync ---

    [Test]
    public async Task CheckDatUpdateAsync_WhenVersionCheckFails_NotifiesError()
    {
        _vm.ActiveDat = MakeDatVMWithUpdateUrl();
        _updateChecker
            .Setup(u =>
                u.FetchLatestVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Result.Fail<string>("network error"));

        await _vm.CheckDatUpdateCommand.ExecuteAsync(null);

        _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task CheckDatUpdateAsync_WhenAlreadyUpToDate_NotifiesInfo()
    {
        _vm.ActiveDat = MakeDatVMWithUpdateUrl(); // DatVersion = 0
        _updateChecker
            .Setup(u =>
                u.FetchLatestVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Result.Ok("0")); // same version → not newer

        await _vm.CheckDatUpdateCommand.ExecuteAsync(null);

        _notifier.Verify(n => n.NotifyInfoAsync(It.IsAny<string>()), Times.Once);
        _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task CheckDatUpdateAsync_WhenUpdateAvailableAndDeclined_DoesNotDownload()
    {
        _vm.ActiveDat = MakeDatVMWithUpdateUrl(); // DatVersion = 0
        _updateChecker
            .Setup(u =>
                u.FetchLatestVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Result.Ok("1")); // newer
        _notifier
            .Setup(n => n.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        await _vm.CheckDatUpdateCommand.ExecuteAsync(null);

        _downloader.Verify(
            d =>
                d.DownloadDatAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task CheckDatUpdateAsync_WhenDownloadFails_NotifiesError()
    {
        _vm.ActiveDat = MakeDatVMWithUpdateUrl(); // DatVersion = 0
        _updateChecker
            .Setup(u =>
                u.FetchLatestVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Result.Ok("1")); // newer
        _notifier
            .Setup(n => n.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _downloader
            .Setup(d =>
                d.DownloadDatAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Fail<string>("network error"));

        await _vm.CheckDatUpdateCommand.ExecuteAsync(null);

        _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task CheckDatUpdateAsync_WhenUpdateSucceedsAndReloadedDatHasImageUrl_ShowsImageDownload()
    {
        _vm.ActiveDat = MakeDatVMWithUpdateUrl(); // DatVersion = 0
        _updateChecker
            .Setup(u =>
                u.FetchLatestVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Result.Ok("1")); // newer
        _notifier
            .Setup(n => n.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _downloader
            .Setup(d =>
                d.DownloadDatAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Ok("/managed/updated.dat"));
        _datReader
            .Setup(r => r.ReadAsync())
            .ReturnsAsync(
                Result.Ok(
                    new DatFile
                    {
                        Header = new DatHeader
                        {
                            DatName = "Test DAT",
                            NewImUrl = "https://example.com/imgs/",
                        },
                        Games = [],
                    }
                )
            );
        _notifier
            .Setup(n =>
                n.ShowImageDownloadAsync(It.IsAny<ImageDownloadWindowVM>(), It.IsAny<Task>())
            )
            .Returns<ImageDownloadWindowVM, Task>((_, task) => task);

        await _vm.CheckDatUpdateCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.ShowImageDownloadAsync(It.IsAny<ImageDownloadWindowVM>(), It.IsAny<Task>()),
            Times.Once
        );
    }

    // --- RenameAllAsync ---

    [Test]
    public async Task RenameAllAsync_WhenGameHasNoScannedRom_DoesNotCallFileOps()
    {
        LoadedDatVM dat = MakeDatVM();
        dat.Games.Add(MakeGameRow(incorrectlyNamed: true)); // ScannedRom is null → no rename target
        _vm.ActiveDat = dat;

        await _vm.RenameAllCommand.ExecuteAsync(null);

        _fileOps.Verify(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // --- ImportDatAsync success path (covers LoadDatFromManagedPathAsync + BuildDatVmAsync) ---

    [Test]
    public async Task ImportDatAsync_WhenSuccessful_AddsDatToLoadedDats()
    {
        DatFile datFile = new DatFile
        {
            Header = new DatHeader { DatName = "Test DAT" },
            Games = [],
        };
        _fileDialogs.Setup(d => d.PickDatFileAsync()).ReturnsAsync("/source/test.dat");
        _datReader.Setup(r => r.ReadAsync()).ReturnsAsync(Result.Ok(datFile));
        _datImporter
            .Setup(i =>
                i.ImportAsync(
                    It.IsAny<string>(),
                    It.IsAny<DatHeader>(),
                    It.IsAny<IProgress<ImportProgress>?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Ok("/managed/test.dat"));

        await _vm.ImportDatCommand.ExecuteAsync(null);

        _vm.LoadedDats.Should().HaveCount(1);
        _vm.ActiveDat.Should().NotBeNull();
        _vm.ActiveDat!.DatName.Should().Be("Test DAT");
    }

    // --- LoadManagedDatsAsync with real DAT (covers BuildDatVmAsync + LastActiveDat logic) ---

    [Test]
    public async Task LoadManagedDatsAsync_WithOneDatOnDisk_SetsActiveDat()
    {
        DatFile datFile = new DatFile
        {
            Header = new DatHeader { DatName = "Managed DAT", System = "GBA" },
            Games = [],
        };

        // Seed the dats directory so GetImportedDatPaths() finds this file
        string fakeXml = Path.Combine(_tempDir, "dats", "managed.xml");
        await File.WriteAllTextAsync(fakeXml, "<dummy/>");

        _datReader.Setup(r => r.ReadAsync()).ReturnsAsync(Result.Ok(datFile));

        await _vm.LoadManagedDatsAsync();

        _vm.LoadedDats.Should().HaveCount(1);
        _vm.ActiveDat.Should().NotBeNull();
        _vm.ActiveDat!.DatName.Should().Be("Managed DAT");
    }

    // --- ScanFolderAsync success path (covers the multi-line match + result persistence) ---

    [Test]
    public async Task ScanFolderAsync_WhenFolderSelected_SetsRomFolderAndUpdatesGames()
    {
        LoadedDatVM dat = MakeDatVM();
        _vm.ActiveDat = dat;
        _fileDialogs.Setup(d => d.PickRomFolderAsync()).ReturnsAsync("/roms/gba");

        // ShowProgressAsync must await the scan task so the test doesn't exit early
        _notifier
            .Setup(n =>
                n.ShowProgressAsync(
                    It.IsAny<string>(),
                    It.IsAny<ProgressWindowVM>(),
                    It.IsAny<Task>()
                )
            )
            .Returns<string, ProgressWindowVM, Task>((_, _, task) => task);

        await _vm.ScanFolderCommand.ExecuteAsync(null);

        _vm.ActiveDat!.RomFolder.Should().Be("/roms/gba");
    }

    // --- LoadDatFromManagedPathAsync failure path ---

    [Test]
    public async Task ImportDatAsync_WhenManagedDatReadFails_NotifiesError()
    {
        DatFile datFile = new DatFile
        {
            Header = new DatHeader { DatName = "Test" },
            Games = [],
        };
        _fileDialogs.Setup(d => d.PickDatFileAsync()).ReturnsAsync("/source/test.dat");
        _datReader
            .SetupSequence(r => r.ReadAsync())
            .ReturnsAsync(Result.Ok(datFile)) // ImportDatAsync source read
            .ReturnsAsync(Result.Fail<DatFile>("managed read error")); // LoadDatFromManagedPath read
        _datImporter
            .Setup(i =>
                i.ImportAsync(
                    It.IsAny<string>(),
                    It.IsAny<DatHeader>(),
                    It.IsAny<IProgress<ImportProgress>?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Ok("/managed/test.dat"));

        await _vm.ImportDatCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("Could not load imported DAT"))),
            Times.Once
        );
        _vm.LoadedDats.Should().BeEmpty();
    }

    // --- ValidateIntegrityAsync — triggered via BuildDatVmAsync when persisted results exist ---

    private static LoadedDatVM MakeDatVMWithRomTitle(string romTitle = "%n") =>
        new LoadedDatVM(
            new DatFile
            {
                Header = new DatHeader { DatName = "Test DAT", RomTitle = romTitle },
                Games = [],
            },
            "/test/dat.xml"
        );

    private static GameRowVM MakeGameRowWithScannedRom(
        string filePath,
        bool incorrectlyNamed = false,
        bool wrongArchiveType = false,
        bool untrimmed = false
    ) =>
        new GameRowVM(
            new MatchResult
            {
                Game = new Game { Title = "Test Game" },
                Status = MatchStatus.Verified,
                IsIncorrectlyNamed = incorrectlyNamed,
                IsWrongArchiveType = wrongArchiveType,
                IsUntrimmed = untrimmed,
                ScannedRom = new ScannedRom
                {
                    FilePath = filePath,
                    FileExtension = Path.GetExtension(filePath).TrimStart('.'),
                },
            },
            string.Empty,
            new DatHeader(),
            []
        );

    // --- RemoveDat command ---

    [Test]
    public void RemoveDatCommand_WhenOneDatLoaded_RemovesDatAndClearsActiveDat()
    {
        LoadedDatVM datVm = MakeDatVM();
        _vm.LoadedDats.Add(datVm);
        _vm.ActiveDat = datVm;

        _vm.RemoveDatCommand.Execute(null);

        _vm.LoadedDats.Should().BeEmpty();
        _vm.ActiveDat.Should().BeNull();
    }

    // --- OpenSettingsAsync ---

    [Test]
    public async Task OpenSettingsAsync_AppliesPersistedFormat_AfterDialogSaves()
    {
        _notifier
            .Setup(n => n.ShowSettingsAsync(It.IsAny<SettingsVM>()))
            .Returns<SettingsVM>(async vm =>
            {
                vm.ArchiveFormat = "zip";
                await vm.SaveCommand.ExecuteAsync(null);
            });

        await _vm.OpenSettingsCommand.ExecuteAsync(null);

        _vm.ArchiveFormat.Should().Be("zip");
    }

    [Test]
    public async Task OpenSettingsAsync_WhenDialogCancelled_DiscardsUnsavedEditsAndKeepsPersistedFormat()
    {
        AppPreferencesService prefs = new AppPreferencesService(
            new AppDataService(_tempDir),
            new LoggerConfiguration().CreateLogger()
        );
        await prefs.UpdateSettingsAsync("zip", null);

        // Cancel: the dialog closes without the VM's SaveCommand ever running.
        _notifier
            .Setup(n => n.ShowSettingsAsync(It.IsAny<SettingsVM>()))
            .Returns<SettingsVM>(vm =>
            {
                vm.ArchiveFormat = "7z";
                return Task.CompletedTask;
            });

        await _vm.OpenSettingsCommand.ExecuteAsync(null);

        _vm.ArchiveFormat.Should().Be("zip");
        (await prefs.LoadAsync()).DefaultArchiveFormat.Should().Be("zip");
    }

    [Test]
    public async Task MoveUnverifiedAsync_WhenSavedFolderSet_SkipsPickerAndUsesIt()
    {
        _notifier
            .Setup(n => n.ShowSettingsAsync(It.IsAny<SettingsVM>()))
            .Returns<SettingsVM>(async vm =>
            {
                vm.UnverifiedFolder = "/dest";
                await vm.SaveCommand.ExecuteAsync(null);
            });
        await _vm.OpenSettingsCommand.ExecuteAsync(null);

        LoadedDatVM dat = MakeDatVM();
        dat.UnmatchedRoms = [new ScannedRom { FilePath = "/roms/unknown.zip" }];
        _vm.ActiveDat = dat;
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());

        await _vm.MoveUnverifiedCommand.ExecuteAsync(null);

        _fileDialogs.Verify(d => d.PickUnverifiedDestinationAsync(), Times.Never);
        _fileOps.Verify(
            f => f.RenameAsync("/roms/unknown.zip", Path.Combine("/dest", "unknown.zip")),
            Times.Once
        );
    }

    // --- TrimAll command CanExecute ---

    [Test]
    public void TrimAllCommand_CannotExecute_WhenNoDatLoaded() =>
        _vm.TrimAllCommand.CanExecute(null).Should().BeFalse();

    [Test]
    public void TrimAllCommand_CannotExecute_WhenNoUntrimmedGames()
    {
        _vm.ActiveDat = MakeDatVM();
        _vm.ActiveDat.Games.Add(MakeGameRow());
        _vm.TrimAllCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void TrimAllCommand_CannotExecute_WhenCompressorUnavailable()
    {
        _vm.ActiveDat = MakeDatVM();
        _vm.ActiveDat.Games.Add(MakeGameRow(untrimmed: true));
        _vm.TrimAllCommand.CanExecute(null).Should().BeFalse();
    }

    [Test]
    public void TrimAllCommand_CanExecute_WhenDatHasUntrimmedGamesAndCompressorAvailable()
    {
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);
        vm.ActiveDat = MakeDatVM();
        vm.ActiveDat.Games.Add(MakeGameRow(untrimmed: true));
        vm.TrimAllCommand.CanExecute(null).Should().BeTrue();
    }

    // --- OnActiveDatGamesChanged ---

    [Test]
    public void OnActiveDatGamesChanged_WhenGameAddedToActiveDat_RaisesCanExecuteChangedOnCommands()
    {
        LoadedDatVM datVm = MakeDatVM();
        _vm.ActiveDat = datVm;
        bool commandNotified = false;
        _vm.RenameAllCommand.CanExecuteChanged += (_, _) => commandNotified = true;

        datVm.Games.Add(MakeGameRow(incorrectlyNamed: true));

        commandNotified.Should().BeTrue();
    }

    // --- RenameSelectedAsync success ---

    [Test]
    public async Task RenameSelectedAsync_WhenRenameSucceeds_ReplacesGameRowInActiveDat()
    {
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());

        LoadedDatVM datVm = MakeDatVMWithRomTitle();
        GameRowVM gameRow = MakeGameRowWithScannedRom(
            "/roms/Wrong Name.7z",
            incorrectlyNamed: true
        );
        datVm.Games.Add(gameRow);
        _vm.ActiveDat = datVm;
        _vm.SelectedGame = gameRow;

        await _vm.RenameSelectedCommand.ExecuteAsync(null);

        datVm.Games[0].Should().NotBeSameAs(gameRow);
    }

    // --- ScanFolderAsync with games in DAT ---

    [Test]
    public async Task ScanFolderAsync_WithDatHavingGames_SetsGamesMissingOnEmptyRomSource()
    {
        LoadedDatVM datVm = new LoadedDatVM(
            new DatFile
            {
                Header = new DatHeader { DatName = "Test DAT" },
                Games = [new Game { Title = "Test Game", ReleaseNumber = 1 }],
            },
            "/test/dat.xml"
        );
        _vm.ActiveDat = datVm;
        _fileDialogs.Setup(d => d.PickRomFolderAsync()).ReturnsAsync("/roms/gba");
        _notifier
            .Setup(n =>
                n.ShowProgressAsync(
                    It.IsAny<string>(),
                    It.IsAny<ProgressWindowVM>(),
                    It.IsAny<Task>()
                )
            )
            .Returns<string, ProgressWindowVM, Task>((_, _, task) => task);

        await _vm.ScanFolderCommand.ExecuteAsync(null);

        datVm.Games.Should().HaveCount(1);
        datVm.Games[0].Status.Should().Be(MatchStatus.Missing);
    }

    // --- ReArchiveSelectedAsync failure path (covers ReArchiveFileAsync + OnIsReArchivingChanged) ---

    [Test]
    public async Task ReArchiveSelectedAsync_WhenCompressFails_NotifiesError()
    {
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        availableCompressor
            .Setup(c =>
                c.CompressAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Fail("compression failed"));
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok("/tmp/no_such_extracted_file.rom"));

        _notifier
            .Setup(n =>
                n.ShowProgressAsync(
                    It.IsAny<string>(),
                    It.IsAny<ProgressWindowVM>(),
                    It.IsAny<Task>()
                )
            )
            .Returns<string, ProgressWindowVM, Task>((_, _, task) => task);

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.zip", wrongArchiveType: true);
        datVm.Games.Add(gameRow);
        vm.ActiveDat = datVm;
        vm.SelectedGame = gameRow;

        await vm.ReArchiveSelectedCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("compression failed"))),
            Times.Once
        );
    }

    [Test]
    public async Task ReArchiveSelectedAsync_WhenInPlaceAndCompressFails_DoesNotDeleteOriginal()
    {
        // In-place re-archive: the source path already matches the target
        // name/extension, so From == To. If compression fails the original ROM
        // must survive — deleting it before the new archive exists loses the file.
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        availableCompressor
            .Setup(c =>
                c.CompressAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Fail("compression failed"));
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok("/tmp/no_such_extracted_file.rom"));
        _fileOps.Setup(f => f.DeleteAsync(It.IsAny<string>())).ReturnsAsync(Result.Ok());

        _notifier
            .Setup(n =>
                n.ShowProgressAsync(
                    It.IsAny<string>(),
                    It.IsAny<ProgressWindowVM>(),
                    It.IsAny<Task>()
                )
            )
            .Returns<string, ProgressWindowVM, Task>((_, _, task) => task);

        LoadedDatVM datVm = MakeDatVM();
        // FilePath already has the target stem (empty naming mask) and the
        // default "7z" extension → GetReArchiveTarget returns From == To.
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.7z", wrongArchiveType: true);
        datVm.Games.Add(gameRow);
        vm.ActiveDat = datVm;
        vm.SelectedGame = gameRow;

        await vm.ReArchiveSelectedCommand.ExecuteAsync(null);

        _fileOps.Verify(f => f.DeleteAsync("/roms/Test.7z"), Times.Never);
        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("compression failed"))),
            Times.Once
        );
    }

    // --- TrimSelectedAsync ---

    [Test]
    public async Task TrimSelectedAsync_WhenExtractFails_NotifiesError()
    {
        _compressor.Setup(c => c.IsAvailable).Returns(true);
        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail("extract failed"));

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.7z", untrimmed: true);
        datVm.Games.Add(gameRow);
        _vm.ActiveDat = datVm;
        _vm.SelectedGame = gameRow;

        await _vm.TrimSelectedCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("extract failed"))),
            Times.Once
        );
    }

    // --- RenameAllAsync ---

    [Test]
    public async Task RenameAllAsync_WhenRenameFails_NotifiesError()
    {
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Fail("rename failed"));

        LoadedDatVM datVm = MakeDatVMWithRomTitle();
        GameRowVM gameRow = MakeGameRowWithScannedRom(
            "/roms/Wrong Name.7z",
            incorrectlyNamed: true
        );
        datVm.Games.Add(gameRow);
        _vm.ActiveDat = datVm;

        await _vm.RenameAllCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("rename failed"))),
            Times.Once
        );
    }

    [Test]
    public async Task RenameAllAsync_WhenRenameSucceeds_ReplacesGameRowInActiveDat()
    {
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());

        LoadedDatVM datVm = MakeDatVMWithRomTitle();
        GameRowVM gameRow = MakeGameRowWithScannedRom(
            "/roms/Wrong Name.7z",
            incorrectlyNamed: true
        );
        datVm.Games.Add(gameRow);
        _vm.ActiveDat = datVm;

        await _vm.RenameAllCommand.ExecuteAsync(null);

        datVm.Games[0].Should().NotBeSameAs(gameRow);
        datVm.Games[0].IsIncorrectlyNamed.Should().BeFalse();
    }

    // --- ReArchiveSelectedAsync extract failure ---

    [Test]
    public async Task ReArchiveSelectedAsync_WhenExtractFails_NotifiesError()
    {
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail("extract failed"));

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.zip", wrongArchiveType: true);
        datVm.Games.Add(gameRow);
        vm.ActiveDat = datVm;
        vm.SelectedGame = gameRow;

        await vm.ReArchiveSelectedCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("extract failed"))),
            Times.Once
        );
    }

    // --- ReArchiveSelectedAsync sameFile delete-original failure ---

    [Test]
    public async Task ReArchiveSelectedAsync_WhenSameFileAndDeleteOriginalFails_NotifiesError()
    {
        // In-place re-archive: compression to the temp archive succeeds, but
        // deleting the original (to swap the new archive in) fails.
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        availableCompressor
            .Setup(c =>
                c.CompressAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Ok());
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok("/tmp/no_such_extracted.rom"));
        _fileOps
            .Setup(f => f.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Fail("delete failed"));

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.7z", wrongArchiveType: true);
        datVm.Games.Add(gameRow);
        vm.ActiveDat = datVm;
        vm.SelectedGame = gameRow;

        await vm.ReArchiveSelectedCommand.ExecuteAsync(null);

        _notifier.Verify(
            n =>
                n.NotifyErrorAsync(
                    It.Is<string>(s =>
                        s.Contains("Could not replace original") && s.Contains("delete failed")
                    )
                ),
            Times.Once
        );
    }

    [Test]
    public async Task ReArchiveSelectedAsync_WhenInPlaceSucceeds_SwapsTempArchiveIntoPlace()
    {
        // In-place re-archive happy path: compress to the temp archive, delete the
        // original, then rename the temp archive onto the final path.
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        availableCompressor
            .Setup(c =>
                c.CompressAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Ok());
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok("/tmp/no_such_extracted.rom"));
        _fileOps.Setup(f => f.DeleteAsync(It.IsAny<string>())).ReturnsAsync(Result.Ok());
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Ok());

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.7z", wrongArchiveType: true);
        datVm.Games.Add(gameRow);
        vm.ActiveDat = datVm;
        vm.SelectedGame = gameRow;

        await vm.ReArchiveSelectedCommand.ExecuteAsync(null);

        // Compression writes to the temp archive, never directly onto the original.
        availableCompressor.Verify(
            c =>
                c.CompressAsync(
                    It.IsAny<string>(),
                    "/roms/Test.7z.romforge-tmp",
                    It.IsAny<long>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        _fileOps.Verify(
            f => f.RenameAsync("/roms/Test.7z.romforge-tmp", "/roms/Test.7z"),
            Times.Once
        );
        _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ReArchiveSelectedAsync_WhenInPlaceRenameFails_NotifiesError()
    {
        // Original already deleted and the new archive sits at the temp path; the
        // rename onto the final name fails, so the temp archive must be preserved.
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        availableCompressor
            .Setup(c =>
                c.CompressAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Ok());
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok("/tmp/no_such_extracted.rom"));
        _fileOps.Setup(f => f.DeleteAsync(It.IsAny<string>())).ReturnsAsync(Result.Ok());
        _fileOps
            .Setup(f => f.RenameAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Fail("rename failed"));

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.7z", wrongArchiveType: true);
        datVm.Games.Add(gameRow);
        vm.ActiveDat = datVm;
        vm.SelectedGame = gameRow;

        await vm.ReArchiveSelectedCommand.ExecuteAsync(null);

        _notifier.Verify(
            n =>
                n.NotifyErrorAsync(
                    It.Is<string>(s =>
                        s.Contains("could not restore final name") && s.Contains("rename failed")
                    )
                ),
            Times.Once
        );
    }

    [Test]
    public async Task ReArchiveSelectedAsync_WhenOperationThrowsUnexpectedly_NotifiesError()
    {
        // A non-cancellation exception thrown mid-operation must be caught and
        // surfaced as an error rather than crashing the app.
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        _notifier
            .Setup(n =>
                n.ShowProgressAsync(
                    It.IsAny<string>(),
                    It.IsAny<ProgressWindowVM>(),
                    It.IsAny<Task>()
                )
            )
            .Returns<string, ProgressWindowVM, Task>((_, _, task) => task);

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.zip", wrongArchiveType: true);
        datVm.Games.Add(gameRow);
        vm.ActiveDat = datVm;
        vm.SelectedGame = gameRow;

        Func<Task> act = async () => await vm.ReArchiveSelectedCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        _notifier.Verify(
            n =>
                n.NotifyErrorAsync(
                    It.Is<string>(s => s.Contains("Re-archive failed unexpectedly"))
                ),
            Times.Once
        );
    }

    [Test]
    public async Task ReArchiveSelectedAsync_WhenInPlaceCompressFails_CleansUpLeftoverTempArchive()
    {
        // In-place re-archive whose temp archive was partially written before the
        // compress failed: the finally block must delete the leftover temp file
        // while leaving the still-intact original untouched.
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string original = Path.Combine(dir, "Test.7z");
            string tempArchive = original + ".romforge-tmp";
            await File.WriteAllBytesAsync(tempArchive, new byte[] { 1, 2, 3 });

            Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
            availableCompressor.Setup(c => c.IsAvailable).Returns(true);
            availableCompressor
                .Setup(c =>
                    c.CompressAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<long>(),
                        It.IsAny<IProgress<int>?>(),
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(Result.Fail("compression failed"));
            MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

            _extractor
                .Setup(e =>
                    e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(Result.Ok("/tmp/no_such_extracted.rom"));
            _fileOps.Setup(f => f.DeleteAsync(It.IsAny<string>())).ReturnsAsync(Result.Ok());

            _notifier
                .Setup(n =>
                    n.ShowProgressAsync(
                        It.IsAny<string>(),
                        It.IsAny<ProgressWindowVM>(),
                        It.IsAny<Task>()
                    )
                )
                .Returns<string, ProgressWindowVM, Task>((_, _, task) => task);

            LoadedDatVM datVm = MakeDatVM();
            GameRowVM gameRow = MakeGameRowWithScannedRom(original, wrongArchiveType: true);
            datVm.Games.Add(gameRow);
            vm.ActiveDat = datVm;
            vm.SelectedGame = gameRow;

            await vm.ReArchiveSelectedCommand.ExecuteAsync(null);

            _fileOps.Verify(f => f.DeleteAsync(tempArchive), Times.Once);
            _fileOps.Verify(f => f.DeleteAsync(original), Times.Never);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // --- ReArchiveAllAsync ---

    [Test]
    public async Task ReArchiveAllAsync_WhenCancelledMidRun_DoesNotNotifyError()
    {
        // A cancellation surfacing from inside the batch loop must propagate as an
        // OperationCanceledException (not be treated as an unexpected failure) and
        // reach the app quietly without notifying an error.
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        LoadedDatVM datVm = MakeDatVM();
        datVm.Games.Add(MakeGameRowWithScannedRom("/roms/Test.zip", wrongArchiveType: true));
        vm.ActiveDat = datVm;

        Func<Task> act = async () => await vm.ReArchiveAllCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ReArchiveAllAsync_WhenExtractFails_NotifiesErrors()
    {
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail("extract failed"));

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.zip", wrongArchiveType: true);
        datVm.Games.Add(gameRow);
        vm.ActiveDat = datVm;

        await vm.ReArchiveAllCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("extract failed"))),
            Times.Once
        );
    }

    // --- TrimSelectedAsync compress failure (covers mid-trim path) ---

    [Test]
    public async Task TrimSelectedAsync_WhenCompressFails_NotifiesError()
    {
        _compressor.Setup(c => c.IsAvailable).Returns(true);
        _compressor
            .Setup(c =>
                c.CompressAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Fail("compress failed"));
        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok("/tmp/no_such_trim.rom"));
        _fileOps
            .Setup(f => f.TruncateAsync(It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(Result.Ok());

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.7z", untrimmed: true);
        datVm.Games.Add(gameRow);
        _vm.ActiveDat = datVm;
        _vm.SelectedGame = gameRow;

        await _vm.TrimSelectedCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("compress failed"))),
            Times.Once
        );
    }

    // --- ReArchiveSelectedAsync: compress succeeds but delete-original fails ---

    [Test]
    public async Task ReArchiveSelectedAsync_WhenDeleteOriginalFails_NotifiesError()
    {
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        availableCompressor
            .Setup(c =>
                c.CompressAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Ok());
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok("/tmp/no_such_extracted.rom"));
        _fileOps
            .Setup(f => f.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(Result.Fail("delete failed"));

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.zip", wrongArchiveType: true);
        datVm.Games.Add(gameRow);
        vm.ActiveDat = datVm;
        vm.SelectedGame = gameRow;

        await vm.ReArchiveSelectedCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("delete failed"))),
            Times.Once
        );
    }

    // --- TrimAllAsync: compress failure mid-trim covers TrimOneAsync compress call ---

    [Test]
    public async Task TrimAllAsync_WhenCompressFails_NotifiesErrors()
    {
        _compressor.Setup(c => c.IsAvailable).Returns(true);
        _compressor
            .Setup(c =>
                c.CompressAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Fail("compress failed"));
        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok("/tmp/no_such_trim.rom"));
        _fileOps
            .Setup(f => f.TruncateAsync(It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(Result.Ok());

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.7z", untrimmed: true);
        datVm.Games.Add(gameRow);
        _vm.ActiveDat = datVm;

        await _vm.TrimAllCommand.ExecuteAsync(null);

        _notifier.Verify(
            n => n.NotifyErrorAsync(It.Is<string>(s => s.Contains("compress failed"))),
            Times.Once
        );
    }

    // --- ReArchiveSelectedAsync success ---

    [Test]
    public async Task ReArchiveSelectedAsync_WhenSucceeds_UpdatesGameRowInActiveDat()
    {
        Mock<IArchiveCompressor> availableCompressor = new Mock<IArchiveCompressor>();
        availableCompressor.Setup(c => c.IsAvailable).Returns(true);
        availableCompressor
            .Setup(c =>
                c.CompressAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<IProgress<int>?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Result.Ok());
        MainWindowVM vm = MakeVM(compressorMock: availableCompressor);

        _extractor
            .Setup(e => e.ExtractToTempFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok("/tmp/no_such_extracted_file.rom"));
        _fileOps.Setup(f => f.DeleteAsync(It.IsAny<string>())).ReturnsAsync(Result.Ok());

        LoadedDatVM datVm = MakeDatVM();
        GameRowVM gameRow = MakeGameRowWithScannedRom("/roms/Test.zip", wrongArchiveType: true);
        datVm.Games.Add(gameRow);
        vm.ActiveDat = datVm;
        vm.SelectedGame = gameRow;

        await vm.ReArchiveSelectedCommand.ExecuteAsync(null);

        datVm.Games[0].Should().NotBeSameAs(gameRow);
        datVm.Games[0].IsWrongArchiveType.Should().BeFalse();
    }

    [Test]
    [NonParallelizable]
    public async Task LoadManagedDatsAsync_WhenPersistedResultsHaveStaleFile_ClearsStaleGame()
    {
        // Seed the scan result store with a verified result whose backing file does not exist
        ILogger logger = new LoggerConfiguration().CreateLogger();
        AppDataService appData = new AppDataService(_tempDir);
        ScanResultStore store = new ScanResultStore(appData, logger);
        await store.InitializeAsync();

        string datName = "Stale DAT";
        Game game = new Game { ReleaseNumber = 1, Title = "Mario" };
        MatchResult staleResult = new MatchResult
        {
            Game = game,
            Status = MatchStatus.Verified,
            ScannedRom = new ScannedRom { FilePath = "/nonexistent/mario.gba" },
        };
        await store.SaveResultsAsync(datName, [staleResult]);

        DatFile datFile = new DatFile
        {
            Header = new DatHeader { DatName = datName, System = "GBA" },
            Games = [game],
        };
        string fakeXml = Path.Combine(_tempDir, "dats", "stale.xml");
        await File.WriteAllTextAsync(fakeXml, "<dummy/>");
        _datReader.Setup(r => r.ReadAsync()).ReturnsAsync(Result.Ok(datFile));

        await _vm.LoadManagedDatsAsync();

        // ValidateIntegrityAsync runs fire-and-forget; give it a moment to complete
        await Task.Delay(500);

        _vm.ActiveDat!.Games[0].Status.Should().Be(MatchStatus.Missing);
    }
}
