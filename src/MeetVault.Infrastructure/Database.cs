using Microsoft.Data.Sqlite;

namespace MeetVault.Infrastructure;

/// <summary>
/// SQLite persistence: owns the connection factory and applies migrations on startup.
/// WAL mode keeps the UI responsive while background jobs write.
/// </summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    public async Task<T> WithConnectionAsync<T>(Func<SqliteConnection, Task<T>> action)
    {
        await using var conn = OpenConnection();
        return await action(conn).ConfigureAwait(false);
    }

    public async Task ApplyMigrationsAsync()
    {
        await using var conn = OpenConnection();
        var applied = new HashSet<string>();
        await using (var select = conn.CreateCommand())
        {
            select.CommandText = "CREATE TABLE IF NOT EXISTS schema_migrations (name TEXT PRIMARY KEY, applied_at TEXT NOT NULL);";
            select.ExecuteNonQuery();
            select.CommandText = "SELECT name FROM schema_migrations;";
            await using var reader = await select.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
                applied.Add(reader.GetString(0));
        }

        foreach (var (name, sql) in Migrations.All)
        {
            if (applied.Contains(name)) continue;
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

                await using var record = conn.CreateCommand();
                record.Transaction = tx;
                record.CommandText = "INSERT INTO schema_migrations(name, applied_at) VALUES ($name, $at);";
                record.Parameters.AddWithValue("$name", name);
                record.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                await record.ExecuteNonQueryAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}

public static class Migrations
{
    /// <summary>Ordered migrations applied at startup.</summary>
    public static readonly (string Name, string Sql)[] All = [.. MigrationScripts.Scripts];
}
