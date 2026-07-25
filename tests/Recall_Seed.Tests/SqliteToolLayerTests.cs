using Microsoft.Data.Sqlite;
using Recall_Seed.Vault;

namespace Recall_Seed.Tests;

// The MCP tool layer (BankTools/SearchTools/TendencyTools) must round-trip through the sqlite backend,
// not just the vault. SqliteStoreTests exercises the store directly; this pins the tools -> store seam.
public sealed class SqliteToolLayerTests : IDisposable
{
    readonly string _dir;
    readonly string _db;

    public SqliteToolLayerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ps-sqlite-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "superstar.db");
        BuildSchema(_db);
        ExperienceStore.Current = new SqliteStore(_db);
    }

    public void Dispose()
    {
        ExperienceStore.Current = new VaultStore();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void WriteExperience_then_read_tools_round_trip_over_sqlite()
    {
        var res = BankTools.WriteExperience(
            id: "2026-07-24-sqlite-tool", title: "Tool over sqlite",
            situation: null, task: null, action: "wrote through the tool layer", result: "landed in sqlite",
            actionConfidence: "high", context: "work",
            skills: [new VaultSkill("sql", "technical")],
            tags: ["backend"],
            metrics: [new VaultMetric("rows", 1, null)],
            entities: ["[[Payments]]"],
            gaps: []);

        Assert.Null(res.Error);
        Assert.Equal("2026-07-24-sqlite-tool", res.Id);

        var got = SearchTools.GetExperience("2026-07-24-sqlite-tool");
        Assert.Null(got.Error);
        Assert.NotNull(got.Experience);
        Assert.Equal("Tool over sqlite", got.Experience!.Title);
        Assert.Equal("wrote through the tool layer", got.Experience.Action);

        Assert.Equal(1, TendencyTools.FindTendencies().Stats.ExperienceCount);
    }

    static void BuildSchema(string db)
    {
        using var conn = new SqliteConnection($"Data Source={db};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Ddl;
        cmd.ExecuteNonQuery();
    }

    const string Ddl = """
        CREATE TABLE experiences (
          id TEXT PRIMARY KEY, title TEXT NOT NULL DEFAULT '', situation TEXT NOT NULL DEFAULT '',
          task TEXT NOT NULL DEFAULT '', action TEXT NOT NULL DEFAULT '', result_text TEXT NOT NULL DEFAULT '',
          context TEXT NOT NULL DEFAULT 'work', happened_start TEXT, happened_end TEXT,
          status TEXT NOT NULL DEFAULT 'draft', created_at TEXT NOT NULL DEFAULT (datetime('now')),
          updated_at TEXT NOT NULL DEFAULT (datetime('now')));
        CREATE TABLE skills (id TEXT PRIMARY KEY, name TEXT UNIQUE NOT NULL, kind TEXT NOT NULL DEFAULT 'technical');
        CREATE TABLE experience_skills (
          experience_id TEXT NOT NULL REFERENCES experiences(id) ON DELETE CASCADE,
          skill_id TEXT NOT NULL REFERENCES skills(id) ON DELETE CASCADE,
          PRIMARY KEY (experience_id, skill_id));
        CREATE TABLE tags (id TEXT PRIMARY KEY, name TEXT UNIQUE NOT NULL);
        CREATE TABLE experience_tags (
          experience_id TEXT NOT NULL REFERENCES experiences(id) ON DELETE CASCADE,
          tag_id TEXT NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
          PRIMARY KEY (experience_id, tag_id));
        CREATE TABLE metrics (id TEXT PRIMARY KEY,
          experience_id TEXT NOT NULL REFERENCES experiences(id) ON DELETE CASCADE,
          label TEXT NOT NULL, value REAL, unit TEXT);
        CREATE TABLE entities (id TEXT PRIMARY KEY, kind TEXT NOT NULL, name TEXT NOT NULL,
          meta_json TEXT, created_at TEXT NOT NULL DEFAULT (datetime('now')), UNIQUE (kind, name));
        CREATE TABLE edges (id TEXT PRIMARY KEY, src_kind TEXT NOT NULL, src_id TEXT NOT NULL,
          rel TEXT NOT NULL, dst_kind TEXT NOT NULL, dst_id TEXT NOT NULL, meta_json TEXT,
          UNIQUE (src_kind, src_id, rel, dst_kind, dst_id));
        CREATE TABLE embed_queue (
          experience_id TEXT PRIMARY KEY REFERENCES experiences(id) ON DELETE CASCADE,
          enqueued_at TEXT NOT NULL DEFAULT (datetime('now')));
        CREATE VIEW v_experiences AS SELECT id, title, situation, task, action, result_text,
          context, status, happened_start, happened_end, created_at, updated_at FROM experiences;
        CREATE VIEW v_experience_skills AS SELECT es.experience_id, s.name AS skill_name, s.kind AS skill_kind
          FROM experience_skills es JOIN skills s ON s.id = es.skill_id;
        CREATE VIEW v_experience_tags AS SELECT et.experience_id, t.name AS tag_name
          FROM experience_tags et JOIN tags t ON t.id = et.tag_id;
        CREATE VIEW v_experience_metrics AS SELECT experience_id, label, value, unit FROM metrics;
        CREATE VIEW v_entities AS SELECT id, kind, name FROM entities;
        CREATE VIEW v_edges AS SELECT src_kind, src_id, rel, dst_kind, dst_id FROM edges;
        """;
}
