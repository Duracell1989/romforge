using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FluentResults;
using RomForge.Core.IO;
using RomForge.Core.Models;
using Serilog;

namespace RomForge.Core.Services;

public sealed class DatConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AppDataService _appData;
    private readonly ILogger _logger;

    public DatConfigService(AppDataService appData, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _appData = appData;
        _logger = logger.ForContext<DatConfigService>();
    }

    /// <summary>
    /// Finds the OfflineList .ini alongside the source DAT, parses language bits, and merges
    /// them into the persisted config for this DAT.
    /// </summary>
    public async Task ImportFromOfflineListAsync(string sourceDatPath, DatHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        string? iniPath = FindOfflineListIni(sourceDatPath, header.DatName);
        if (iniPath is null)
        {
            _logger.Debug("No OfflineList .ini found for {DatName}", header.DatName);
            return;
        }

        Result<OfflineListConfig> readResult = OfflineListConfigReader.Read(iniPath);
        if (readResult.IsFailed)
        {
            _logger.Warning(
                "Could not read OfflineList config for {DatName}: {Error}",
                header.DatName,
                readResult.Errors[0].Message
            );
            return;
        }

        OfflineListConfig ini = readResult.Value;
        DatConfig existing = await LoadAsync(header.DatName) ?? new DatConfig();

        DatConfig updated = existing with { LanguageBits = [.. ini.LanguageBits] };

        await SaveAsync(header.DatName, updated);
        _logger.Information(
            "Saved config for {DatName}: {Count} language bits",
            header.DatName,
            updated.LanguageBits.Count
        );
    }

    public async Task<DatConfig?> LoadAsync(string datName)
    {
        string path = GetConfigFilePath(datName);
        if (!File.Exists(path))
            return null;

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<DatConfig>(stream, JsonOptions);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warning(ex, "Could not load config for {DatName}", datName);
            return null;
        }
    }

    public async Task SaveAsync(string datName, DatConfig config)
    {
        string path = GetConfigFilePath(datName);
        try
        {
            await using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write);
            await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warning(ex, "Could not save config for {DatName}", datName);
        }
    }

    public async Task UpdateRomFolderAsync(string datName, string romFolderPath)
    {
        DatConfig existing = await LoadAsync(datName) ?? new DatConfig();
        await SaveAsync(datName, existing with { RomFolderPath = romFolderPath });
    }

    private string GetConfigFilePath(string datName) =>
        Path.Combine(_appData.ConfigPath, SanitizeName(datName) + ".json");

    private static string? FindOfflineListIni(string sourceDatPath, string datName)
    {
        string? datDir = Path.GetDirectoryName(sourceDatPath);
        if (datDir is null)
            return null;
        string? rootDir = Path.GetDirectoryName(datDir);
        if (rootDir is null)
            return null;
        string iniPath = Path.Combine(rootDir, "config", $"{datName}.ini");
        return File.Exists(iniPath) ? iniPath : null;
    }

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    private static string SanitizeName(string name) =>
        string.Concat(
            System.Linq.Enumerable.Select(name, c => Array.IndexOf(InvalidChars, c) >= 0 ? '_' : c)
        );
}
