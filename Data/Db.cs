using Microsoft.Data.Sqlite;

namespace PersonalServer.Data;

/// <summary>
/// Resolves and opens the STARfolio SQLite database (superstar.db) that PersonalServer
/// reads from and writes to. The shared read/write contract — views, writable tables,
/// pragmas — is documented in SCHEMA-CONTRACT.md.
/// </summary>
public static class Db
{
    public const string PathEnvVar = "SUPERSTAR_DB_PATH";

    public static string ResolveDbPath()
    {
        var overridePath = Environment.GetEnvironmentVariable(PathEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "STARfolio", "superstar.db");
    }

    public static bool Exists(string? path = null) => File.Exists(path ?? ResolveDbPath());

    /// <summary>
    /// Read-only connection. Opened read-write at the file level with PRAGMA query_only,
    /// not SqliteOpenMode.ReadOnly: a WAL database needs shared-memory write access to read
    /// while STARfolio has it open, so a file-level read-only handle can fail mid-read. query_only
    /// gives logical read-only (writes rejected) while letting SQLite manage the WAL index.
    /// </summary>
    public static SqliteConnection OpenReadOnly(string? path = null)
    {
        var conn = Open(path ?? ResolveDbPath(), SqliteOpenMode.ReadWrite);
        Exec(conn, "PRAGMA query_only = ON;");
        return conn;
    }

    public static SqliteConnection OpenReadWrite(string? path = null)
        => Open(path ?? ResolveDbPath(), SqliteOpenMode.ReadWrite);

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
        conn.Open();
        Exec(conn, "PRAGMA busy_timeout = 5000;");
        Exec(conn, "PRAGMA foreign_keys = ON;");
        return conn;
    }

    static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Thrown when the STARfolio database file cannot be found at the resolved path.</summary>
public sealed class DbNotFoundException(string path)
    : Exception($"STARfolio database not found at '{path}'. Set {Db.PathEnvVar} or launch STARfolio once to create it.")
{
    public string Path { get; } = path;
}
