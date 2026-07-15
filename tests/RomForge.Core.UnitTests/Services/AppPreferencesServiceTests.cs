using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Models;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.UnitTests.Services;

[TestOf(typeof(AppPreferencesService))]
public sealed class AppPreferencesServiceTests
{
    private string _tempDir = string.Empty;
    private AppDataService _appData = null!;
    private AppPreferencesService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _appData = new AppDataService(_tempDir);
        _svc = new AppPreferencesService(_appData, new LoggerConfiguration().CreateLogger());
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    [Test]
    public async Task LoadAsync_WhenNoFile_ReturnsDefaultPreferences()
    {
        AppPreferences result = await _svc.LoadAsync();

        result.LastActiveDatName.Should().BeNull();
        result.DefaultArchiveFormat.Should().Be("7z");
        result.UnverifiedFolder.Should().BeNull();
    }

    [Test]
    public async Task UpdateSettingsAsync_PersistsFormatAndFolder()
    {
        await _svc.UpdateSettingsAsync("zip", "/roms/unverified");

        AppPreferences loaded = await _svc.LoadAsync();
        loaded.DefaultArchiveFormat.Should().Be("zip");
        loaded.UnverifiedFolder.Should().Be("/roms/unverified");
    }

    [Test]
    public async Task UpdateSettingsAsync_ClearsFolder_WhenNull()
    {
        await _svc.UpdateSettingsAsync("7z", "/roms/unverified");

        await _svc.UpdateSettingsAsync("zip", null);

        AppPreferences loaded = await _svc.LoadAsync();
        loaded.UnverifiedFolder.Should().BeNull();
        loaded.DefaultArchiveFormat.Should().Be("zip");
    }

    [Test]
    public async Task UpdateSettingsAsync_PreservesLastActiveDatName()
    {
        await _svc.UpdateLastActiveDatAsync("GBA");

        await _svc.UpdateSettingsAsync("zip", "/roms/unverified");

        AppPreferences loaded = await _svc.LoadAsync();
        loaded.LastActiveDatName.Should().Be("GBA");
    }

    [Test]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsLastActiveDatName()
    {
        AppPreferences prefs = new AppPreferences
        {
            LastActiveDatName = "GBA - Official OfflineList",
        };

        await _svc.SaveAsync(prefs);
        AppPreferences loaded = await _svc.LoadAsync();

        loaded.LastActiveDatName.Should().Be("GBA - Official OfflineList");
    }

    [Test]
    public async Task UpdateLastActiveDatAsync_SetsName()
    {
        await _svc.UpdateLastActiveDatAsync("Nintendo DS");

        AppPreferences loaded = await _svc.LoadAsync();
        loaded.LastActiveDatName.Should().Be("Nintendo DS");
    }

    [Test]
    public async Task UpdateLastActiveDatAsync_ClearsName_WhenNull()
    {
        await _svc.SaveAsync(
            new AppPreferences { LastActiveDatName = "GBA - Official OfflineList" }
        );

        await _svc.UpdateLastActiveDatAsync(null);

        AppPreferences loaded = await _svc.LoadAsync();
        loaded.LastActiveDatName.Should().BeNull();
    }

    [Test]
    public async Task UpdateLastActiveDatAsync_PreservesOtherFields_WhenFileAlreadyExists()
    {
        await _svc.SaveAsync(new AppPreferences { LastActiveDatName = "Old DAT" });

        await _svc.UpdateLastActiveDatAsync("New DAT");

        AppPreferences loaded = await _svc.LoadAsync();
        loaded.LastActiveDatName.Should().Be("New DAT");
    }

    [Test]
    public async Task ConcurrentLastActiveDatUpdates_DoNotClobberOtherSettings()
    {
        // The UI fires UpdateLastActiveDatAsync fire-and-forget on every DAT switch; several can
        // overlap. Each does a load-modify-save on the shared file, so without serialization a
        // load that hits the file mid-write falls back to defaults and persists them, wiping the
        // user's format/folder/startup settings.
        await _svc.SaveAsync(
            new AppPreferences
            {
                DefaultArchiveFormat = "zip",
                UnverifiedFolder = "/roms/unverified",
                CheckForUpdatesOnStartup = false,
            }
        );

        IEnumerable<Task> updates = Enumerable
            .Range(0, 100)
            .Select(i => _svc.UpdateLastActiveDatAsync($"dat-{i}"));
        await Task.WhenAll(updates);

        AppPreferences loaded = await _svc.LoadAsync();
        loaded.DefaultArchiveFormat.Should().Be("zip");
        loaded.UnverifiedFolder.Should().Be("/roms/unverified");
        loaded.CheckForUpdatesOnStartup.Should().BeFalse();
    }

    [Test]
    public async Task UpdateLastActiveDatAsync_WhenFileUnreadable_DoesNotClobberItWithDefaults()
    {
        // A transiently unreadable file (locked by a concurrent writer, or mid-write) must not be
        // overwritten with defaults — that is what wipes the user's real settings. The update is
        // skipped and the file is left intact for the next successful read.
        string path = Path.Combine(_appData.ConfigPath, "preferences.json");
        const string unreadable = "{ not valid json ]]]";
        await File.WriteAllTextAsync(path, unreadable);

        await _svc.UpdateLastActiveDatAsync("New DAT");

        string after = await File.ReadAllTextAsync(path);
        after.Should().Be(unreadable);
    }

    [Test]
    public async Task LoadAsync_WhenFileIsCorrupt_ReturnsDefaultPreferences()
    {
        string path = Path.Combine(_appData.ConfigPath, "preferences.json");
        await File.WriteAllTextAsync(path, "{ this is not valid json }}}");

        AppPreferences result = await _svc.LoadAsync();

        result.LastActiveDatName.Should().BeNull();
    }
}
