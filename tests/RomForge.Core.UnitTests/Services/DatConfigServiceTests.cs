using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Models;
using RomForge.Core.Services;
using Serilog;

namespace RomForge.Core.UnitTests.Services;

[TestOf(typeof(DatConfigService))]
public sealed class DatConfigServiceTests
{
    private string _tempDir = string.Empty;
    private AppDataService _appData = null!;
    private DatConfigService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _appData = new AppDataService(_tempDir);
        _svc = new DatConfigService(_appData, new LoggerConfiguration().CreateLogger());
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tempDir, recursive: true);

    [Test]
    public async Task LoadAsync_MissingFile_ReturnsNull()
    {
        DatConfig? result = await _svc.LoadAsync("NonExistentDat");

        result.Should().BeNull();
    }

    [Test]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsRomFolder()
    {
        DatConfig config = new DatConfig { RomFolderPath = "/roms/GBA" };

        await _svc.SaveAsync("TestDat", config);
        DatConfig? loaded = await _svc.LoadAsync("TestDat");

        loaded.Should().NotBeNull();
        loaded!.RomFolderPath.Should().Be("/roms/GBA");
    }

    [Test]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsLanguageBits()
    {
        DatConfig config = new DatConfig
        {
            LanguageBits = new List<LanguageBit>
            {
                new LanguageBit(BitIndex: 1, Label: "En"),
                new LanguageBit(BitIndex: 8, Label: "JP"),
            },
        };

        await _svc.SaveAsync("TestDat", config);
        DatConfig? loaded = await _svc.LoadAsync("TestDat");

        loaded!.LanguageBits.Should().HaveCount(2);
        loaded.LanguageBits[0].BitIndex.Should().Be(1);
        loaded.LanguageBits[0].Label.Should().Be("En");
        loaded.LanguageBits[1].BitIndex.Should().Be(8);
        loaded.LanguageBits[1].Label.Should().Be("JP");
    }

    [Test]
    public async Task UpdateRomFolderAsync_NoExistingConfig_CreatesConfigWithFolder()
    {
        await _svc.UpdateRomFolderAsync("TestDat", "/roms/GBA");

        DatConfig? loaded = await _svc.LoadAsync("TestDat");
        loaded!.RomFolderPath.Should().Be("/roms/GBA");
    }

    [Test]
    public async Task UpdateRomFolderAsync_ExistingConfig_UpdatesOnlyRomFolder()
    {
        DatConfig initial = new DatConfig
        {
            LanguageBits = new List<LanguageBit> { new LanguageBit(1, "En") },
        };
        await _svc.SaveAsync("TestDat", initial);

        await _svc.UpdateRomFolderAsync("TestDat", "/roms/GBA");

        DatConfig? loaded = await _svc.LoadAsync("TestDat");
        loaded!.RomFolderPath.Should().Be("/roms/GBA");
        loaded.LanguageBits.Should().HaveCount(1);
    }

    [Test]
    public async Task ImportFromOfflineListAsync_ValidIni_PersistsLanguageBits()
    {
        // Set up OfflineList directory layout: {root}/dats/{datName}.zip + {root}/config/{datName}.ini
        string datName = "GBA";
        string sourceDatDir = Path.Combine(_tempDir, "source", "dats");
        string configDir = Path.Combine(_tempDir, "source", "config");
        Directory.CreateDirectory(sourceDatDir);
        Directory.CreateDirectory(configDir);

        string sourceDatPath = Path.Combine(sourceDatDir, $"{datName}.zip");
        await File.WriteAllTextAsync(sourceDatPath, string.Empty);

        string iniContent = "[Option]\nl1=\"En\"\nl8=\"JP\"\n";
        await File.WriteAllTextAsync(Path.Combine(configDir, $"{datName}.ini"), iniContent);

        DatHeader header = new DatHeader { DatName = datName };
        await _svc.ImportFromOfflineListAsync(sourceDatPath, header);

        DatConfig? loaded = await _svc.LoadAsync(datName);
        loaded.Should().NotBeNull();
        loaded!.LanguageBits.Should().HaveCount(2);
    }

    [Test]
    public async Task ImportFromOfflineListAsync_NoIniFile_DoesNotCreateConfig()
    {
        string sourceDatDir = Path.Combine(_tempDir, "source", "dats");
        Directory.CreateDirectory(sourceDatDir);
        string sourceDatPath = Path.Combine(sourceDatDir, "GBA.zip");
        await File.WriteAllTextAsync(sourceDatPath, string.Empty);

        DatHeader header = new DatHeader { DatName = "GBA" };
        await _svc.ImportFromOfflineListAsync(sourceDatPath, header);

        DatConfig? loaded = await _svc.LoadAsync("GBA");
        loaded.Should().BeNull();
    }
}
