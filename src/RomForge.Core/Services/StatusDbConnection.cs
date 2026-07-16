using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace RomForge.Core.Services;

/// <summary>
/// Opens connections to the shared status database with WAL journalling and a busy timeout.
/// Parallel operations (e.g. "Re-Archive All" running several tasks at once) each open their own
/// connection to the same file; without this a writer can hit SQLITE_BUSY and have its write
/// silently swallowed, so a re-archived ROM would reappear as "needs work" on the next scan.
/// </summary>
internal static class StatusDbConnection
{
    private const int BusyTimeoutMs = 5000;

    public static async Task<SqliteConnection> OpenAsync(string connectionString)
    {
        SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using SqliteCommand pragma = connection.CreateCommand();
        // journal_mode=WAL persists on the database file; busy_timeout is per-connection and makes
        // a contended writer wait for the lock rather than failing immediately.
        // CA2100: the only interpolated value is the private const BusyTimeoutMs, never user input.
#pragma warning disable CA2100
        pragma.CommandText = $"PRAGMA journal_mode=WAL; PRAGMA busy_timeout={BusyTimeoutMs};";
#pragma warning restore CA2100
        await pragma.ExecuteNonQueryAsync();

        return connection;
    }
}
