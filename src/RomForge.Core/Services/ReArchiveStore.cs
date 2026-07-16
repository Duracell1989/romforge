using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog;

namespace RomForge.Core.Services;

/// <summary>
/// Persists which ROMs have been re-archived with optimal settings.
/// This store survives re-scans; the scan result store is cleared on each scan.
/// </summary>
public sealed class ReArchiveStore
{
    private const string ParamDatName = "@DatName";
    private const string ParamReleaseNumber = "@ReleaseNumber";
    private const string ParamArchivedAt = "@ArchivedAt";

    private readonly string _connectionString;
    private readonly ILogger _logger;

    public ReArchiveStore(AppDataService appData, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(appData);
        ArgumentNullException.ThrowIfNull(logger);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = appData.StatusDbPath,
        }.ToString();
        _logger = logger.ForContext<ReArchiveStore>();
    }

    public async Task InitializeAsync()
    {
        await using var conn = await StatusDbConnection.OpenAsync(_connectionString);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"
            CREATE TABLE IF NOT EXISTS ReArchivedRoms (
                DatName       TEXT    NOT NULL,
                ReleaseNumber INTEGER NOT NULL,
                ArchivedAt    TEXT    NOT NULL,
                PRIMARY KEY (DatName, ReleaseNumber)
            );";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkAsync(string datName, int releaseNumber)
    {
        try
        {
            await using var conn = await StatusDbConnection.OpenAsync(_connectionString);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                @"
                INSERT OR REPLACE INTO ReArchivedRoms (DatName, ReleaseNumber, ArchivedAt)
                VALUES (@DatName, @ReleaseNumber, @ArchivedAt)";
            cmd.Parameters.AddWithValue(ParamDatName, datName);
            cmd.Parameters.AddWithValue(ParamReleaseNumber, releaseNumber);
            cmd.Parameters.AddWithValue(ParamArchivedAt, DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqliteException ex)
        {
            _logger.Warning(
                ex,
                "Could not mark {DatName}/{ReleaseNumber} as re-archived",
                datName,
                releaseNumber
            );
        }
    }

    public async Task<HashSet<int>> GetReArchivedReleasesAsync(string datName)
    {
        var result = new HashSet<int>();
        try
        {
            await using SqliteConnection conn = await StatusDbConnection.OpenAsync(
                _connectionString
            );
            await using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ReleaseNumber FROM ReArchivedRoms WHERE DatName = @DatName";
            cmd.Parameters.AddWithValue(ParamDatName, datName);
            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(reader.GetInt32(0));
        }
        catch (SqliteException ex)
        {
            _logger.Warning(ex, "Could not load re-archived releases for {DatName}", datName);
        }
        return result;
    }
}
