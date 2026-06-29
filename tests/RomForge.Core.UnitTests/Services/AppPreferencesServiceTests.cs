using System;
using System.IO;
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
    }

    [Test]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsLastActiveDatName()
    {
        AppPreferences prefs = new AppPreferences { LastActiveDatName = "GBA - Official OfflineList" };

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
        await _svc.SaveAsync(new AppPreferences { LastActiveDatName = "GBA - Official OfflineList" });

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
    public async Task LoadAsync_WhenFileIsCorrupt_ReturnsDefaultPreferences()
    {
        string path = Path.Combine(_appData.ConfigPath, "preferences.json");
        await File.WriteAllTextAsync(path, "{ this is not valid json }}}");

        AppPreferences result = await _svc.LoadAsync();

        result.LastActiveDatName.Should().BeNull();
    }
}
