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

namespace RomForge.UI.UnitTests.ViewModels
{
    [TestOf(typeof(SettingsVm))]
    public sealed class SettingsVmTests
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
            _preferencesService.Dispose();
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private SettingsVm MakeVm(AppPreferences? current = null) => new SettingsVm(_preferencesService, _fileDialogs.Object, current ?? new AppPreferences());

        [Test]
        public void Constructor_InitializesFromCurrentPreferences()
        {
            SettingsVm vm = MakeVm(new AppPreferences { DefaultArchiveFormat = "zip", UnverifiedFolder = "/roms/unv" });

            vm.ArchiveFormat.Should().Be("zip");
            vm.UnverifiedFolder.Should().Be("/roms/unv");
        }

        [Test]
        public void ArchiveFormats_Contains7zAndZip()
        {
            SettingsVm vm = MakeVm();

            vm.ArchiveFormats.Should().BeEquivalentTo("7z", "zip");
        }

        [Test]
        public void Constructor_InitializesCheckForUpdatesOnStartup_FromCurrentPreferences()
        {
            SettingsVm vm = MakeVm(new AppPreferences { CheckForUpdatesOnStartup = false });

            vm.CheckForUpdatesOnStartup.Should().BeFalse();
        }

        [Test]
        public void CheckForUpdatesOnStartup_DefaultsToTrue()
        {
            SettingsVm vm = MakeVm();

            vm.CheckForUpdatesOnStartup.Should().BeTrue();
        }

        [Test]
        public async Task Save_PersistsCheckForUpdatesOnStartup()
        {
            SettingsVm vm = MakeVm();
            vm.CheckForUpdatesOnStartup = false;

            await vm.SaveCommand.ExecuteAsync(null);

            (await _preferencesService.LoadAsync()).CheckForUpdatesOnStartup.Should().BeFalse();
        }

        [Test]
        public async Task BrowseUnverifiedFolder_WhenPicked_SetsFolder()
        {
            _fileDialogs.Setup(d => d.PickUnverifiedDestinationAsync()).ReturnsAsync("/picked");
            SettingsVm vm = MakeVm();

            await vm.BrowseUnverifiedFolderCommand.ExecuteAsync(null);

            vm.UnverifiedFolder.Should().Be("/picked");
        }

        [Test]
        public async Task BrowseUnverifiedFolder_WhenCancelled_LeavesFolderUnchanged()
        {
            _fileDialogs.Setup(d => d.PickUnverifiedDestinationAsync()).ReturnsAsync((string?)null);
            SettingsVm vm = MakeVm(new AppPreferences { UnverifiedFolder = "/existing" });

            await vm.BrowseUnverifiedFolderCommand.ExecuteAsync(null);

            vm.UnverifiedFolder.Should().Be("/existing");
        }

        [Test]
        public void ClearUnverifiedFolder_SetsNull()
        {
            SettingsVm vm = MakeVm(new AppPreferences { UnverifiedFolder = "/existing" });

            vm.ClearUnverifiedFolderCommand.Execute(null);

            vm.UnverifiedFolder.Should().BeNull();
        }

        [Test]
        public async Task Save_PersistsPreferencesAndRequestsCloseWithTrue()
        {
            SettingsVm vm = MakeVm();
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
            SettingsVm vm = MakeVm();
            vm.ArchiveFormat = "zip";
            bool? closedWith = null;
            vm.RequestClose = result => closedWith = result;

            vm.CancelCommand.Execute(null);

            closedWith.Should().BeFalse();
            AppPreferences loaded = await _preferencesService.LoadAsync();
            loaded.DefaultArchiveFormat.Should().Be("7z");
        }
    }
}
