using Microsoft.Data.Sqlite;

namespace PersonalServer.Tests;

/// <summary>
/// Builds a faithful subset of a STARfolio superstar.db — the base tables the contract touches,
/// the experiences FTS table + triggers, all seven v_* contract views, and a small seeded graph
/// — so the read tools can be exercised without STARfolio present.
/// </summary>
internal static class Fixture
{
    public static string Create(string dir)
    {
        var path = Path.Combine(dir, "superstar.db");
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        using var conn = new SqliteConnection(cs);
        conn.Open();
        Exec(conn, "PRAGMA journal_mode = WAL;");
        Exec(conn, Schema);
        Exec(conn, Seed);
        return path;
    }

    static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    const string Schema = """
        CREATE TABLE experiences (
          id TEXT PRIMARY KEY, title TEXT NOT NULL DEFAULT '', situation TEXT NOT NULL DEFAULT '',
          task TEXT NOT NULL DEFAULT '', action TEXT NOT NULL DEFAULT '', result_text TEXT NOT NULL DEFAULT '',
          context TEXT NOT NULL DEFAULT 'work', happened_start TEXT, happened_end TEXT,
          status TEXT NOT NULL DEFAULT 'draft', draft_state_json TEXT,
          created_at TEXT NOT NULL DEFAULT (datetime('now')), updated_at TEXT NOT NULL DEFAULT (datetime('now')));
        CREATE TABLE skills (id TEXT PRIMARY KEY, name TEXT UNIQUE NOT NULL, kind TEXT NOT NULL DEFAULT 'technical');
        CREATE TABLE experience_skills (experience_id TEXT NOT NULL, skill_id TEXT NOT NULL, PRIMARY KEY (experience_id, skill_id));
        CREATE TABLE tags (id TEXT PRIMARY KEY, name TEXT UNIQUE NOT NULL);
        CREATE TABLE experience_tags (experience_id TEXT NOT NULL, tag_id TEXT NOT NULL, PRIMARY KEY (experience_id, tag_id));
        CREATE TABLE metrics (id TEXT PRIMARY KEY, experience_id TEXT NOT NULL, label TEXT NOT NULL, value REAL, unit TEXT);
        CREATE TABLE sources (id TEXT PRIMARY KEY, kind TEXT NOT NULL, uri_or_path TEXT, attachment_path TEXT, title TEXT, raw_text TEXT, meta_json TEXT, content_hash TEXT, ingested_at TEXT);
        CREATE TABLE experience_sources (experience_id TEXT NOT NULL, source_id TEXT NOT NULL, PRIMARY KEY (experience_id, source_id));
        CREATE TABLE entities (id TEXT PRIMARY KEY, kind TEXT NOT NULL, name TEXT NOT NULL, meta_json TEXT, created_at TEXT, UNIQUE (kind, name));
        CREATE TABLE edges (id TEXT PRIMARY KEY, src_kind TEXT NOT NULL, src_id TEXT NOT NULL, rel TEXT NOT NULL, dst_kind TEXT NOT NULL, dst_id TEXT NOT NULL, meta_json TEXT, UNIQUE (src_kind, src_id, rel, dst_kind, dst_id));

        CREATE VIRTUAL TABLE experiences_fts USING fts5(title, situation, task, action, result_text, content='experiences', content_rowid='rowid');
        CREATE TRIGGER experiences_fts_ai AFTER INSERT ON experiences BEGIN
          INSERT INTO experiences_fts(rowid, title, situation, task, action, result_text)
          VALUES (new.rowid, new.title, new.situation, new.task, new.action, new.result_text);
        END;

        CREATE VIEW v_experiences AS
          SELECT id, title, situation, task, action, result_text, context, status,
                 happened_start, happened_end, created_at, updated_at FROM experiences;
        CREATE VIEW v_experience_skills AS
          SELECT es.experience_id, s.name AS skill_name, s.kind AS skill_kind
          FROM experience_skills es JOIN skills s ON s.id = es.skill_id;
        CREATE VIEW v_experience_tags AS
          SELECT et.experience_id, t.name AS tag_name FROM experience_tags et JOIN tags t ON t.id = et.tag_id;
        CREATE VIEW v_experience_metrics AS
          SELECT experience_id, label, value, unit FROM metrics;
        CREATE VIEW v_entities AS SELECT id, kind, name FROM entities;
        CREATE VIEW v_edges AS SELECT src_kind, src_id, rel, dst_kind, dst_id FROM edges;
        CREATE VIEW v_experience_sources AS
          SELECT xs.experience_id, s.kind AS source_kind, s.uri_or_path, s.title
          FROM experience_sources xs JOIN sources s ON s.id = xs.source_id;
        """;

    const string Seed = """
        INSERT INTO experiences (id, title, action, context, status, happened_start) VALUES
          ('a', 'Led database migration', 'coordinated the cutover', 'work', 'confirmed', '2025-03-01'),
          ('b', 'Mentored intern on SQL', 'paired daily', 'work', 'confirmed', '2025-05-01'),
          ('c', 'Built React dashboard', 'shipped the UI', 'project', 'draft', '2025-06-01');
        INSERT INTO skills (id, name, kind) VALUES
          ('sk1', 'leadership', 'soft'), ('sk2', 'sql', 'technical'), ('sk3', 'react', 'technical');
        INSERT INTO experience_skills (experience_id, skill_id) VALUES
          ('a', 'sk1'), ('a', 'sk2'), ('b', 'sk1'), ('b', 'sk2'), ('c', 'sk3');
        INSERT INTO tags (id, name) VALUES ('tg1', 'backend');
        INSERT INTO experience_tags (experience_id, tag_id) VALUES ('a', 'tg1');
        INSERT INTO metrics (id, experience_id, label, value, unit) VALUES ('m1', 'a', 'downtime', 0, 'min');
        INSERT INTO entities (id, kind, name) VALUES ('p1', 'project', 'Payments Platform'), ('t1', 'tool', 'PostgreSQL');
        INSERT INTO edges (id, src_kind, src_id, rel, dst_kind, dst_id) VALUES
          ('e1', 'experience', 'a', 'mentions', 'entity', 'p1'),
          ('e2', 'experience', 'a', 'mentions', 'entity', 't1'),
          ('e3', 'experience', 'b', 'mentions', 'entity', 'p1');
        """;
}
