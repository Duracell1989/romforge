using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using NUnit.Framework;
using RomForge.Core.Models;
using RomForge.Core.Services;
using RomForge.UI.Services;
using RomForge.UI.ViewModels;
using Serilog;

namespace RomForge.UI.UnitTests.ViewModels;

[TestOf(typeof(SettingsVM))]
public sealed class SettingsVMTests
{
    private string _tempDir = null!;
    private AppPreferencesService _preferencesService = null!;
    private Mock<IFileDialogService> _fileDialogs = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        AppDataService appData = new AppDataService(_tempDir);
        ILogger logger = new LoggerConfiguration().CreateLogger();
        _preferencesService = new AppPreferencesService(appData, logger);
        _fileDialogs = new Mock<IFileDialogService>();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private SettingsVM MakeVM(AppPreferences? current = null) =>
        new SettingsVM(_preferencesService, _fileDialogs.Object, current ?? new AppPreferences());

    [Test]
    public void Constructor_InitializesFromCurrentPreferences()
    {
        SettingsVM vm = MakeVM(
            new AppPreferences { DefaultArchiveFormat = "zip", UnverifiedFolder = "/roms/unv" }
        );

        vm.ArchiveFormat.Should().Be("zip");
        vm.UnverifiedFolder.Should().Be("/roms/unv");
    }

    [Test]
    public void ArchiveFormats_Contains7zAndZip()
    {
        SettingsVM vm = MakeVM();

        vm.ArchiveFormats.Should().BeEquivalentTo("7z", "zip");
    }

    [Test]
    public void Constructor_InitializesCheckForUpdatesOnStartup_FromCurrentPreferences()
    {
        SettingsVM vm = MakeVM(new AppPreferences { CheckForUpdatesOnStartup = false });

        vm.CheckForUpdatesOnStartup.Should().BeFalse();
    }

    [Test]
    public void CheckForUpdatesOnStartup_DefaultsToTrue()
    {
        SettingsVM vm = MakeVM();

        vm.CheckForUpdatesOnStartup.Should().BeTrue();
    }

    [Test]
    public async Task Save_PersistsCheckForUpdatesOnStartup()
    {
        SettingsVM vm = MakeVM();
        vm.CheckForUpdatesOnStartup = false;

        await vm.SaveCommand.ExecuteAsync(null);

        (await _preferencesService.LoadAsync()).CheckForUpdatesOnStartup.Should().BeFalse();
    }

    [Test]
    public async Task BrowseUnverifiedFolder_WhenPicked_SetsFolder()
    {
        _fileDialogs.Setup(d => d.PickUnverifiedDestinationAsync()).ReturnsAsync("/picked");
        SettingsVM vm = MakeVM();

        await vm.BrowseUnverifiedFolderCommand.ExecuteAsync(null);

        vm.UnverifiedFolder.Should().Be("/picked");
    }

    [Test]
    public async Task BrowseUnverifiedFolder_WhenCancelled_LeavesFolderUnchanged()
    {
        _fileDialogs.Setup(d => d.PickUnverifiedDestinationAsync()).ReturnsAsync((string?)null);
        SettingsVM vm = MakeVM(new AppPreferences { UnverifiedFolder = "/existing" });

        await vm.BrowseUnverifiedFolderCommand.ExecuteAsync(null);

        vm.UnverifiedFolder.Should().Be("/existing");
    }

    [Test]
    public void ClearUnverifiedFolder_SetsNull()
    {
        SettingsVM vm = MakeVM(new AppPreferences { UnverifiedFolder = "/existing" });

        vm.ClearUnverifiedFolderCommand.Execute(null);

        vm.UnverifiedFolder.Should().BeNull();
    }

    [Test]
    public async Task Save_PersistsPreferencesAndRequestsCloseWithTrue()
    {
        SettingsVM vm = MakeVM();
        vm.ArchiveFormat = "zip";
        vm.UnverifiedFolder = "/dest";
        bool? closedWith = null;
        vm.RequestClose = result => closedWith = result;

        await vm.SaveCommand.ExecuteAsync(null);

        closedWith.Should().BeTrue();
        AppPreferences loaded = await _preferencesService.LoadAsync();
        loaded.DefaultArchiveFormat.Should().Be("zip");
        loaded.UnverifiedFolder.Should().Be("/dest");
    }

    [Test]
    public async Task Cancel_DoesNotPersistAndRequestsCloseWithFalse()
    {
        await _preferencesService.UpdateSettingsAsync("7z", null);
        SettingsVM vm = MakeVM();
        vm.ArchiveFormat = "zip";
        bool? closedWith = null;
        vm.RequestClose = result => closedWith = result;

        vm.CancelCommand.Execute(null);

        closedWith.Should().BeFalse();
        AppPreferences loaded = await _preferencesService.LoadAsync();
        loaded.DefaultArchiveFormat.Should().Be("7z");
    }
}
