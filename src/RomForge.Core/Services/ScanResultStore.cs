using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.Core.Scanning;
using Serilog;

namespace RomForge.Core.Services;

public sealed class ScanResultStore
{
    private const string ParamDatName = "@DatName";

    private readonly string _connectionString;
    private readonly ILogger _logger;

    public ScanResultStore(AppDataService appData, ILogger logger)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = appData.StatusDbPath,
        }.ToString();
        _logger = logger.ForContext<ScanResultStore>();
    }

    public async Task InitializeAsync()
    {
        await using SqliteConnection conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ScanResults (
                    DatName             TEXT    NOT NULL,
                    ReleaseNumber       INTEGER NOT NULL,
                    Status              INTEGER NOT NULL,
                    FilePath            TEXT,
                    FileExtension       TEXT,
                    RomExtension        TEXT,
                    Crc                 INTEGER,
                    IsIncorrectlyNamed  INTEGER NOT NULL DEFAULT 0,
                    IsWrongArchiveType  INTEGER NOT NULL DEFAULT 0,
                    IsUntrimmed         INTEGER NOT NULL DEFAULT 0,
                    IsReArchived        INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (DatName, ReleaseNumber)
                );";
            await cmd.ExecuteNonQueryAsync();
        }

        await MigrateAsync(conn);
    }

    /// <summary>
    /// Replaces all persisted results for <paramref name="datName"/> in a single transaction.
    /// </summary>
    public async Task SaveResultsAsync(string datName, IReadOnlyList<MatchResult> results)
    {
        try
        {
            await using SqliteConnection conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            await using SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync();

            await using (SqliteCommand del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM ScanResults WHERE DatName = @DatName";
                del.Parameters.AddWithValue(ParamDatName, datName);
                await del.ExecuteNonQueryAsync();
            }

            await using (SqliteCommand ins = conn.CreateCommand())
            {
                ins.CommandText = @"
                    INSERT INTO ScanResults
                        (DatName, ReleaseNumber, Status, FilePath, FileExtension, RomExtension, Crc,
                         IsIncorrectlyNamed, IsWrongArchiveType, IsUntrimmed, IsReArchived)
                    VALUES
                        (@DatName, @ReleaseNumber, @Status, @FilePath, @FileExtension, @RomExtension, @Crc,
                         @IsIncorrectlyNamed, @IsWrongArchiveType, @IsUntrimmed, @IsReArchived)";

                SqliteParameter pDatName = ins.Parameters.Add(ParamDatName, SqliteType.Text);
                SqliteParameter pRelNum = ins.Parameters.Add("@ReleaseNumber", SqliteType.Integer);
                SqliteParameter pStatus = ins.Parameters.Add("@Status", SqliteType.Integer);
                SqliteParameter pFilePath = ins.Parameters.Add("@FilePath", SqliteType.Text);
                SqliteParameter pFileExt = ins.Parameters.Add("@FileExtension", SqliteType.Text);
                SqliteParameter pRomExt = ins.Parameters.Add("@RomExtension", SqliteType.Text);
                SqliteParameter pCrc = ins.Parameters.Add("@Crc", SqliteType.Integer);
                SqliteParameter pIncorrectlyNamed = ins.Parameters.Add("@IsIncorrectlyNamed", SqliteType.Integer);
                SqliteParameter pWrongArchive = ins.Parameters.Add("@IsWrongArchiveType", SqliteType.Integer);
                SqliteParameter pUntrimmed = ins.Parameters.Add("@IsUntrimmed", SqliteType.Integer);
                SqliteParameter pReArchived = ins.Parameters.Add("@IsReArchived", SqliteType.Integer);

                foreach (MatchResult r in results)
                {
                    pDatName.Value = datName;
                    pRelNum.Value = r.Game.ReleaseNumber;
                    pStatus.Value = (int)r.Status;
                    pFilePath.Value = (object?)r.ScannedRom?.FilePath ?? DBNull.Value;
                    pFileExt.Value = (object?)r.ScannedRom?.FileExtension ?? DBNull.Value;
                    pRomExt.Value = (object?)r.ScannedRom?.RomExtension ?? DBNull.Value;
                    pCrc.Value = r.ScannedRom is not null ? (object)(long)r.ScannedRom.Crc : DBNull.Value;
                    pIncorrectlyNamed.Value = r.IsIncorrectlyNamed ? 1 : 0;
                    pWrongArchive.Value = r.IsWrongArchiveType ? 1 : 0;
                    pUntrimmed.Value = r.IsUntrimmed ? 1 : 0;
                    pReArchived.Value = r.IsReArchived ? 1 : 0;
                    await ins.ExecuteNonQueryAsync();
                }
            }

            await tx.CommitAsync();
            _logger.Debug("Saved {Count} scan results for {DatName}", results.Count, datName);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not save scan results for {DatName}", datName);
        }
    }

    /// <summary>
    /// Loads persisted results for <paramref name="datName"/> and merges them with the DAT's
    /// current game list. Games not present in the DB are returned as Missing.
    /// Returns an empty list when no results have been saved yet.
    /// </summary>
    public async Task<IReadOnlyList<MatchResult>> LoadResultsAsync(string datName, DatFile datFile)
    {
        Dictionary<int, PersistedRow> rows;
        try
        {
            rows = await ReadRowsAsync(datName);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not load scan results for {DatName}", datName);
            return [];
        }

        if (rows.Count == 0)
            return [];

        List<MatchResult> results = new List<MatchResult>(datFile.Games.Count);
        foreach (Game game in datFile.Games)
            results.Add(BuildMatchResult(game, rows));
        return results;
    }

    private async Task<Dictionary<int, PersistedRow>> ReadRowsAsync(string datName)
    {
        Dictionary<int, PersistedRow> rows = new Dictionary<int, PersistedRow>();
        await using SqliteConnection conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ReleaseNumber, Status, FilePath, FileExtension, RomExtension, Crc,
                   IsIncorrectlyNamed, IsWrongArchiveType, IsUntrimmed, IsReArchived
            FROM ScanResults
            WHERE DatName = @DatName";
        cmd.Parameters.AddWithValue(ParamDatName, datName);

        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int releaseNumber = reader.GetInt32(0);
            MatchStatus status = (MatchStatus)reader.GetInt32(1);
            string? filePath = await reader.IsDBNullAsync(2) ? null : reader.GetString(2);
            string? fileExt = await reader.IsDBNullAsync(3) ? null : reader.GetString(3);
            string? romExt = await reader.IsDBNullAsync(4) ? null : reader.GetString(4);
            uint? crc = await reader.IsDBNullAsync(5) ? null : (uint)reader.GetInt64(5);
            bool isIncorrectlyNamed = reader.GetInt32(6) != 0;
            bool isWrongArchiveType = reader.GetInt32(7) != 0;
            bool isUntrimmed = reader.GetInt32(8) != 0;
            bool isReArchived = reader.GetInt32(9) != 0;
            rows[releaseNumber] = new PersistedRow(
                status, filePath, fileExt, romExt, crc,
                isIncorrectlyNamed, isWrongArchiveType, isUntrimmed, isReArchived
            );
        }

        return rows;
    }

    private static MatchResult BuildMatchResult(Game game, Dictionary<int, PersistedRow> rows)
    {
        if (!rows.TryGetValue(game.ReleaseNumber, out PersistedRow? row) || row is null)
            return new MatchResult { Game = game, Status = MatchStatus.Missing, ScannedRom = null };

        ScannedRom? scannedRom = row.FilePath is not null
            ? new ScannedRom
            {
                FilePath = row.FilePath,
                FileExtension = row.FileExtension ?? string.Empty,
                RomExtension = row.RomExtension ?? string.Empty,
                Crc = row.Crc ?? 0,
            }
            : null;

        return new MatchResult
        {
            Game = game,
            Status = row.Status,
            ScannedRom = scannedRom,
            IsIncorrectlyNamed = row.IsIncorrectlyNamed,
            IsWrongArchiveType = row.IsWrongArchiveType,
            IsUntrimmed = row.IsUntrimmed,
            IsReArchived = row.IsReArchived,
        };
    }

    /// <summary>
    /// Upserts a single result. Called after rename, re-archive, or trim operations.
    /// </summary>
    public async Task UpdateResultAsync(string datName, MatchResult result)
    {
        try
        {
            await using SqliteConnection conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            await using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO ScanResults
                    (DatName, ReleaseNumber, Status, FilePath, FileExtension, RomExtension, Crc,
                     IsIncorrectlyNamed, IsWrongArchiveType, IsUntrimmed, IsReArchived)
                VALUES
                    (@DatName, @ReleaseNumber, @Status, @FilePath, @FileExtension, @RomExtension, @Crc,
                     @IsIncorrectlyNamed, @IsWrongArchiveType, @IsUntrimmed, @IsReArchived)";
            cmd.Parameters.AddWithValue(ParamDatName, datName);
            cmd.Parameters.AddWithValue("@ReleaseNumber", result.Game.ReleaseNumber);
            cmd.Parameters.AddWithValue("@Status", (int)result.Status);
            cmd.Parameters.AddWithValue("@FilePath",
                (object?)result.ScannedRom?.FilePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FileExtension",
                (object?)result.ScannedRom?.FileExtension ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RomExtension",
                (object?)result.ScannedRom?.RomExtension ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Crc",
                result.ScannedRom is not null ? (object)(long)result.ScannedRom.Crc : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsIncorrectlyNamed", result.IsIncorrectlyNamed ? 1 : 0);
            cmd.Parameters.AddWithValue("@IsWrongArchiveType", result.IsWrongArchiveType ? 1 : 0);
            cmd.Parameters.AddWithValue("@IsUntrimmed", result.IsUntrimmed ? 1 : 0);
            cmd.Parameters.AddWithValue("@IsReArchived", result.IsReArchived ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not update scan result for {DatName}/{ReleaseNumber}",
                datName, result.Game.ReleaseNumber);
        }
    }

    private async Task MigrateAsync(SqliteConnection conn)
    {
        bool hasNewSchema = false;
        await using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(ScanResults)";
            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.GetString(1) == "IsIncorrectlyNamed")
                {
                    hasNewSchema = true;
                    break;
                }
            }
        }

        if (hasNewSchema)
            return;

        // Old schema had Status values: 0=Verified, 1=IncorrectlyNamed, 2=WrongArchiveType,
        // 3=Missing, 4=Untrimmed. New schema: 0=Missing, 1=Verified with boolean flags.
        string[] statements =
        [
            "ALTER TABLE ScanResults ADD COLUMN IsIncorrectlyNamed INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE ScanResults ADD COLUMN IsWrongArchiveType INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE ScanResults ADD COLUMN IsUntrimmed INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE ScanResults ADD COLUMN IsReArchived INTEGER NOT NULL DEFAULT 0",
            "UPDATE ScanResults SET IsIncorrectlyNamed = 1 WHERE Status = 1",
            "UPDATE ScanResults SET IsWrongArchiveType = 1 WHERE Status = 2",
            "UPDATE ScanResults SET IsUntrimmed = 1 WHERE Status = 4",
            "UPDATE ScanResults SET Status = (CASE WHEN Status = 3 THEN 0 ELSE 1 END)",
        ];

        await using (SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync())
        {
            foreach (string sql in statements)
            {
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }

        _logger.Information("Migrated ScanResults table to flag-based schema");
    }

    private sealed record PersistedRow(
        MatchStatus Status,
        string? FilePath,
        string? FileExtension,
        string? RomExtension,
        uint? Crc,
        bool IsIncorrectlyNamed,
        bool IsWrongArchiveType,
        bool IsUntrimmed,
        bool IsReArchived
    );
}
