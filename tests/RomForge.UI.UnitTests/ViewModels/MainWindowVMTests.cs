using System;
using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using Moq;
using NUnit.Framework;
using RomForge.Core.IO;
using RomForge.Core.Matching;
using RomForge.Core.Models;
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

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
        compressorMock ??= new Mock<IArchiveCompressor>();

        return new MainWindowVM(
            new Mock<IFileDialogService>().Object,
            _ => new Mock<IDatReader>().Object,
            new Mock<IRomSource>().Object,
            new Mock<IRomFileOperations>().Object,
            compressorMock.Object,
            new Mock<IArchiveExtractor>().Object,
            new Mock<IUserNotifier>().Object,
            logger,
            appData,
            new Mock<IDatImporter>().Object,
            new Mock<IDatUpdateChecker>().Object,
            new Mock<IDatDownloader>().Object,
            new DatConfigService(appData, logger),
            new ScanResultStore(appData, logger),
            new ReArchiveStore(appData, logger),
            new AppPreferencesService(appData, logger)
        );
    }

    private static LoadedDatVM MakeDatVM() =>
        new LoadedDatVM(
            new DatFile { Header = new DatHeader { DatName = "Test DAT" }, Games = [] },
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
    public void ArchiveFormat_OnConstruction_Is7z() =>
        _vm.ArchiveFormat.Should().Be("7z");

    [Test]
    public void LoadedDats_OnConstruction_IsEmpty() =>
        _vm.LoadedDats.Should().BeEmpty();

    [Test]
    public void IsDatLoaded_OnConstruction_IsFalse() =>
        _vm.IsDatLoaded.Should().BeFalse();

    [Test]
    public void StatusSummary_OnConstruction_IsNoDatLoaded() =>
        _vm.StatusSummary.Should().Be("No DAT loaded");

    [Test]
    public void MoveUnverifiedLabel_OnConstruction_ShowsZeroCount() =>
        _vm.MoveUnverifiedLabel.Should().Be("Move Unverified (0)");

    [Test]
    public void ArchiveFormats_Contains7zAndZip() =>
        _vm.ArchiveFormats.Should().BeEquivalentTo("7z", "zip");

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
}
