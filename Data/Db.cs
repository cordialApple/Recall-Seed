using Microsoft.Data.Sqlite;

namespace Recall_Seed.Data;

/// <summary>
/// Resolves and opens the STARfolio SQLite database (superstar.db) that the sqlite backend reads
/// from and writes to. The shared read/write contract (views, writable tables, pragmas) is
/// documented in docs/architecture/db-contract.md.
/// </summary>
internal static class Db
{
    public const string PathEnvVar = "SUPERSTAR_DB_PATH";

    public static string ResolveDbPath(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var overridePath = Environment.GetEnvironmentVariable(PathEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "STARfolio", "superstar.db");
    }

    public static SqliteConnection OpenReadOnly(string? path = null)
    {
        var conn = Open(ResolveDbPath(path), SqliteOpenMode.ReadWrite);
        try
        {
            // query_only, not SqliteOpenMode.ReadOnly: a WAL database needs shared-memory write
            // access to read while STARfolio has it open, so a file-level read-only handle can
            // fail mid-read. query_only gives logical read-only while SQLite manages the WAL index.
            Exec(conn, "PRAGMA query_only = ON;");
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    public static SqliteConnection OpenReadWrite(string? path = null)
        => Open(ResolveDbPath(path), SqliteOpenMode.ReadWrite);

    static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        if (!File.Exists(path))
            throw new DbNotFoundException(path);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
            Cache = SqliteCacheMode.Private
        }.ToString();

        var conn = new SqliteConnection(connectionString);
        try
        {
            conn.Open();
            Exec(conn, "PRAGMA busy_timeout = 5000;");
            Exec(conn, "PRAGMA foreign_keys = ON;");
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

internal sealed class DbNotFoundException(string path)
    : Exception($"STARfolio database not found at '{path}'. Set {Db.PathEnvVar} or launch STARfolio once to create it.")
{
    public string Path { get; } = path;
}
