using Microsoft.Data.Sqlite;
using PersonalServer.Data;

namespace PersonalServer.Tests;

public class DbTests : IDisposable
{
    readonly string _dir;
    readonly string _dbPath;

    public DbTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "psrv-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "superstar.db");
        SeedFixture(_dbPath);
    }

    static void SeedFixture(string path)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        Exec(conn, "PRAGMA journal_mode = WAL;");
        Exec(conn, """
            CREATE TABLE experiences (
              id TEXT PRIMARY KEY, title TEXT NOT NULL DEFAULT '', situation TEXT NOT NULL DEFAULT '',
              task TEXT NOT NULL DEFAULT '', action TEXT NOT NULL DEFAULT '', result_text TEXT NOT NULL DEFAULT '',
              context TEXT NOT NULL DEFAULT 'work', status TEXT NOT NULL DEFAULT 'draft',
              happened_start TEXT, happened_end TEXT,
              created_at TEXT NOT NULL DEFAULT (datetime('now')), updated_at TEXT NOT NULL DEFAULT (datetime('now')));
            """);
        Exec(conn, """
            CREATE VIEW v_experiences AS
              SELECT id, title, situation, task, action, result_text, context, status,
                     happened_start, happened_end, created_at, updated_at FROM experiences;
            """);
        Exec(conn, "INSERT INTO experiences (id, title, action, status) VALUES ('e1', 'Led migration', 'coordinated team', 'confirmed');");
    }

    [Fact]
    public void ReadOnly_reads_view()
    {
        using var conn = Db.OpenReadOnly(_dbPath);
        Assert.Equal("Led migration", ScalarStr(conn, "SELECT title FROM v_experiences WHERE id = 'e1';"));
    }

    [Fact]
    public void ReadOnly_sets_contract_pragmas()
    {
        using var conn = Db.OpenReadOnly(_dbPath);
        Assert.Equal(1, ScalarLong(conn, "PRAGMA query_only;"));
        Assert.Equal(5000, ScalarLong(conn, "PRAGMA busy_timeout;"));
        Assert.Equal(1, ScalarLong(conn, "PRAGMA foreign_keys;"));
        Assert.Equal("wal", ScalarStr(conn, "PRAGMA journal_mode;"));
    }

    [Fact]
    public void ReadOnly_rejects_writes()
        => Assert.Throws<SqliteException>(() =>
        {
            using var conn = Db.OpenReadOnly(_dbPath);
            Exec(conn, "INSERT INTO experiences (id, title) VALUES ('e2', 'x');");
        });

    [Fact]
    public void ReadWrite_can_insert()
    {
        using (var conn = Db.OpenReadWrite(_dbPath))
            Exec(conn, "INSERT INTO experiences (id, title) VALUES ('e3', 'added');");
        using var check = Db.OpenReadOnly(_dbPath);
        Assert.Equal(1, ScalarLong(check, "SELECT COUNT(*) FROM experiences WHERE id = 'e3';"));
    }

    [Fact]
    public void Resolve_honors_env_override()
    {
        Environment.SetEnvironmentVariable(Db.PathEnvVar, _dbPath);
        try { Assert.Equal(_dbPath, Db.ResolveDbPath()); }
        finally { Environment.SetEnvironmentVariable(Db.PathEnvVar, null); }
    }

    [Fact]
    public void Missing_file_throws_typed()
    {
        var ex = Assert.Throws<DbNotFoundException>(() => Db.OpenReadOnly(Path.Combine(_dir, "nope.db")));
        Assert.Contains("nope.db", ex.Message);
    }

    static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static long ScalarLong(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    static string? ScalarStr(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
