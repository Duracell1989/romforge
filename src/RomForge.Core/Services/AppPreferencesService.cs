using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RomForge.Core.Models;
using Serilog;

namespace RomForge.Core.Services;

/// <summary>
/// Persists application-level preferences to a JSON file in the RomForge config directory.
/// All reads and writes are serialized so overlapping updates cannot corrupt the file or lose
/// each other's changes.
/// </summary>
public sealed class AppPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private readonly AppDataService _appData;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    public AppPreferencesService(AppDataService appData, ILogger logger)
    {
        _appData = appData;
        _logger = logger.ForContext<AppPreferencesService>();
    }

    public async Task<AppPreferences> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await ReadUnlockedAsync() ?? new AppPreferences();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppPreferences preferences)
    {
        await _gate.WaitAsync();
        try
        {
            await WriteUnlockedAsync(preferences);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateLastActiveDatAsync(string? datName) =>
        await ModifyAsync(existing => existing with { LastActiveDatName = datName });

    public async Task UpdateSettingsAsync(
        string defaultArchiveFormat,
        string? unverifiedFolder,
        bool checkForUpdatesOnStartup = true
    ) =>
        await ModifyAsync(existing =>
            existing with
            {
                DefaultArchiveFormat = defaultArchiveFormat,
                UnverifiedFolder = unverifiedFolder,
                CheckForUpdatesOnStartup = checkForUpdatesOnStartup,
            }
        );

    /// <summary>
    /// Loads, applies <paramref name="mutate"/>, and saves as a single atomic step under the write
    /// gate. If a preferences file exists but cannot be read, the update is skipped rather than
    /// overwriting good settings with defaults.
    /// </summary>
    private async Task ModifyAsync(Func<AppPreferences, AppPreferences> mutate)
    {
        await _gate.WaitAsync();
        try
        {
            AppPreferences? existing = await ReadUnlockedAsync();
            if (existing is null)
            {
                _logger.Warning("Skipping preferences update; the existing file could not be read");
                return;
            }

            await WriteUnlockedAsync(mutate(existing));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads the preferences file. Returns a fresh <see cref="AppPreferences"/> when no file
    /// exists yet, or <c>null</c> when a file exists but could not be read or parsed.
    /// </summary>
    private async Task<AppPreferences?> ReadUnlockedAsync()
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
            return null;
        }
    }

    /// <summary>
    /// Writes preferences to a temporary file and moves it into place, so a crash mid-write cannot
    /// leave a truncated preferences file behind.
    /// </summary>
    private async Task WriteUnlockedAsync(AppPreferences preferences)
    {
        string path = GetPath();
        string tempPath = path + ".tmp";
        try
        {
            await using (FileStream stream = File.Open(tempPath, FileMode.Create, FileAccess.Write))
            {
                await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not save app preferences");
            TryDeleteTemp(tempPath);
        }
    }

    private void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(ex, "Could not remove temporary preferences file");
        }
    }

    private string GetPath() => Path.Combine(_appData.ConfigPath, "preferences.json");
}
