using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using RomForge.Core.Models;
using Serilog;

namespace RomForge.Core.Services;

/// <summary>
/// Persists application-level preferences to a JSON file in the RomForge config directory.
/// </summary>
public sealed class AppPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private readonly AppDataService _appData;
    private readonly ILogger _logger;

    public AppPreferencesService(AppDataService appData, ILogger logger)
    {
        _appData = appData;
        _logger = logger.ForContext<AppPreferencesService>();
    }

    public async Task<AppPreferences> LoadAsync()
    {
        string path = GetPath();
        if (!File.Exists(path))
            return new AppPreferences();

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AppPreferences>(stream, JsonOptions)
                ?? new AppPreferences();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not load app preferences");
            return new AppPreferences();
        }
    }

    public async Task SaveAsync(AppPreferences preferences)
    {
        string path = GetPath();
        try
        {
            await using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write);
            await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not save app preferences");
        }
    }

    public async Task UpdateLastActiveDatAsync(string? datName)
    {
        AppPreferences existing = await LoadAsync();
        await SaveAsync(existing with { LastActiveDatName = datName });
    }

    public async Task UpdateSettingsAsync(string defaultArchiveFormat, string? unverifiedFolder)
    {
        AppPreferences existing = await LoadAsync();
        await SaveAsync(
            existing with
            {
                DefaultArchiveFormat = defaultArchiveFormat,
                UnverifiedFolder = unverifiedFolder,
            }
        );
    }

    private string GetPath() => Path.Combine(_appData.ConfigPath, "preferences.json");
}
