using Microsoft.Data.Sqlite;
using Recall_Seed.Vault;

namespace Recall_Seed.Tests;

public sealed class SqliteStoreTests : IDisposable
{
    readonly string _dir;
    readonly string _db;

    public SqliteStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ps-sqlite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "superstar.db");
        BuildSchema(_db);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    static (string, string, string)[] Beats(string s, string t, string a, string r) =>
        [("situation", s, "high"), ("task", t, "high"), ("action", a, "high"), ("result", r, "high")];

    [Fact]
    public void Write_then_load_round_trips()
    {
        var store = new SqliteStore(_db);
        var (id, _, err) = store.Write(
            "exp-1", "Shipped the thing", "work", "confirmed",
            Beats("sit", "tsk", "act", "res"),
            [new VaultSkill("C#", "technical"), new VaultSkill("Leadership", "soft")],
            ["backend", "launch"],
            [new VaultMetric("latency", 42, "ms")],
            ["[[Acme]]"], ["a gap"]);

        Assert.Null(err);
        Assert.Equal("exp-1", id);

        var (items, loadErr) = store.LoadAll();
        Assert.Null(loadErr);
        var e = Assert.Single(items);
        Assert.Equal("exp-1", e.Id);
        Assert.Equal("Shipped the thing", e.Title);
        Assert.Equal("work", e.Context);
        Assert.Equal("confirmed", e.Status);
        Assert.Equal("sit", e.Situation);
        Assert.Equal("tsk", e.Task);
        Assert.Equal("act", e.Action);
        Assert.Equal("res", e.Result);
        Assert.Equal(2, e.Skills.Count);
        Assert.Contains(e.Skills, s => s.Name == "Leadership" && s.Kind == "soft");
        Assert.Equal(2, e.Tags.Count);
        var m = Assert.Single(e.Metrics);
        Assert.Equal("latency", m.Label);
        Assert.Equal(42, m.Value);
        Assert.Equal("ms", m.Unit);
    }

    [Fact]
    public void Status_drives_confidence()
    {
        var store = new SqliteStore(_db);
        store.Write("d", "d", "work", "draft", Beats("s", "t", "a", "r"), [], [], [], [], []);
        store.Write("c", "c", "work", "confirmed", Beats("s", "t", "a", "r"), [], [], [], [], []);

        var items = store.LoadAll().Items.ToDictionary(x => x.Id);
        Assert.Equal("medium", items["d"].Confidence["action"]);
        Assert.Equal("high", items["c"].Confidence["result"]);
    }

    [Fact]
    public void Empty_beat_reports_low_confidence_even_when_confirmed()
    {
        var store = new SqliteStore(_db);
        store.Write("e", "e", "work", "confirmed",
            [("situation", "sit", "high"), ("task", "tsk", "high"), ("action", "", "high"), ("result", "  ", "high")],
            [], [], [], [], []);
        var e = Assert.Single(store.LoadAll().Items);
        Assert.Equal("high", e.Confidence["situation"]);
        Assert.Equal("low", e.Confidence["action"]);
        Assert.Equal("low", e.Confidence["result"]);
    }

    [Fact]
    public void Write_is_upsert_and_replaces_children()
    {
        var store = new SqliteStore(_db);
        store.Write("x", "v1", "work", "draft", Beats("s", "t", "a", "r"),
            [new VaultSkill("Go", "technical")], ["old"], [new VaultMetric("m1", 1, null)], [], []);
        store.Write("x", "v2", "work", "confirmed", Beats("s2", "t2", "a2", "r2"),
            [new VaultSkill("Rust", "technical")], ["new"], [new VaultMetric("m2", 2, null)], [], []);

        var e = Assert.Single(store.LoadAll().Items);
        Assert.Equal("v2", e.Title);
        Assert.Equal("s2", e.Situation);
        Assert.Equal("Rust", Assert.Single(e.Skills).Name);
        Assert.Equal("new", Assert.Single(e.Tags));
        Assert.Equal("m2", Assert.Single(e.Metrics).Label);
    }

    [Fact]
    public void Skill_kind_is_preserved_on_read()
    {
        var store = new SqliteStore(_db);
        store.Write("k", "k", "work", "draft", Beats("s", "t", "a", "r"),
            [new VaultSkill("Mentoring", "soft")], [], [], [], []);
        var e = Assert.Single(store.LoadAll().Items);
        Assert.Equal("soft", Assert.Single(e.Skills).Kind);
    }

    [Fact]
    public void Entities_and_known_entities_read_from_graph()
    {
        SeedEntity(_db, "ent-acme", "company", "Acme");
        SeedEntity(_db, "ent-beta", "company", "Beta");
        var store = new SqliteStore(_db);
        store.Write("g", "g", "work", "draft", Beats("s", "t", "a", "r"), [], [], [], [], []);
        SeedEdge(_db, "edge-1", "experience", "g", "mentions", "entity", "ent-acme");

        var e = Assert.Single(store.LoadAll().Items);
        Assert.Equal("[[Acme]]", Assert.Single(e.Entities));

        Assert.Equal(new[] { "Acme", "Beta" }, store.KnownEntities());
    }

    [Fact]
    public void Missing_db_returns_clean_errors_without_throwing()
    {
        var store = new SqliteStore(Path.Combine(_dir, "nope.db"));
        var (items, err) = store.LoadAll();
        Assert.Empty(items);
        Assert.NotNull(err);
        Assert.Empty(store.KnownEntities());
        var (_, _, writeErr) = store.Write("z", "z", "work", "draft", Beats("s", "t", "a", "r"), [], [], [], [], []);
        Assert.NotNull(writeErr);
    }

    static void SeedEntity(string db, string id, string kind, string name)
        => Exec(db, "INSERT INTO entities (id, kind, name) VALUES (@id, @kind, @name)",
            ("@id", id), ("@kind", kind), ("@name", name));

    static void SeedEdge(string db, string id, string sk, string si, string rel, string dk, string di)
        => Exec(db, "INSERT INTO edges (id, src_kind, src_id, rel, dst_kind, dst_id) VALUES (@id,@sk,@si,@rel,@dk,@di)",
            ("@id", id), ("@sk", sk), ("@si", si), ("@rel", rel), ("@dk", dk), ("@di", di));

    static void Exec(string db, string sql, params (string, string)[] ps)
    {
        using var conn = new SqliteConnection($"Data Source={db};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();
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
