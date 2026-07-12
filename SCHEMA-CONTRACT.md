# STARfolio ↔ PersonalServer — Schema Contract

PersonalServer reads and writes STARfolio's SQLite database (`superstar.db`). To keep the two
repos decoupled, PersonalServer couples only to the surface defined here, not to STARfolio's raw
table layout. STARfolio owns this contract and pledges to keep it stable across its own
migrations; either side changing it changes this file, deliberately, on both sides.

Verified against STARfolio schema **migration 6** (`006_corpus_practice.sql`); contract views
land as STARfolio migration **7** (`007_contract_views.sql`).

## Locating the database

Resolution order (see `Data/Db.cs`):

1. `SUPERSTAR_DB_PATH` environment variable (set in the Claude Desktop server config), else
2. platform default: `%APPDATA%\STARfolio\superstar.db` (Electron `userData` for productName
   `STARfolio`).

A missing file surfaces as a typed `DbNotFoundException` naming the resolved path — tools turn
that into a structured `{ error }`, never a crash over stdio.

## Connection pragmas

Both processes open the file in **WAL** (STARfolio sets `journal_mode = WAL` on init) and set:

- `busy_timeout = 5000` — serialize the two potential writers under WAL.
- `foreign_keys = ON` — enforce referential integrity on writes.

**Read tools open read-write at the file level plus `PRAGMA query_only = ON`**, *not*
`SqliteOpenMode.ReadOnly`. A WAL database needs shared-memory write access to be read while
STARfolio has it open, so a file-level read-only handle can fail mid-read; `query_only` gives
logical read-only while letting SQLite manage the WAL index. Write tools open plain read-write.

## Read contract — views (STARfolio owns the DDL)

These are the only objects read tools query. STARfolio may refactor the underlying tables freely
as long as these columns stay shaped as below. Canonical DDL (used verbatim by STARfolio's
`007_contract_views.sql`):

```sql
DROP VIEW IF EXISTS v_experiences;
CREATE VIEW v_experiences AS
  SELECT id, title, situation, task, action, result_text,
         context, status, happened_start, happened_end, created_at, updated_at
  FROM experiences;

DROP VIEW IF EXISTS v_experience_skills;
CREATE VIEW v_experience_skills AS
  SELECT es.experience_id, s.name AS skill_name, s.kind AS skill_kind
  FROM experience_skills es JOIN skills s ON s.id = es.skill_id;

DROP VIEW IF EXISTS v_experience_tags;
CREATE VIEW v_experience_tags AS
  SELECT et.experience_id, t.name AS tag_name
  FROM experience_tags et JOIN tags t ON t.id = et.tag_id;

DROP VIEW IF EXISTS v_experience_metrics;
CREATE VIEW v_experience_metrics AS
  SELECT experience_id, label, value, unit FROM metrics;

DROP VIEW IF EXISTS v_entities;
CREATE VIEW v_entities AS
  SELECT id, kind, name FROM entities;

DROP VIEW IF EXISTS v_edges;
CREATE VIEW v_edges AS
  SELECT src_kind, src_id, rel, dst_kind, dst_id FROM edges;

DROP VIEW IF EXISTS v_experience_sources;
CREATE VIEW v_experience_sources AS
  SELECT xs.experience_id, s.kind AS source_kind, s.uri_or_path, s.title
  FROM experience_sources xs JOIN sources s ON s.id = xs.source_id;
```

**Keyword search does not use a view.** FTS5 `MATCH` must reference the real table
`experiences_fts` (external-content over `title, situation, task, action, result_text`, kept in
sync by triggers). That table is part of the contract as a named, stable object. Match-query
construction: split input on whitespace, quote each token, append `*` to the last token,
`ORDER BY rank` ascending (best first). To resolve a hit's `id`/`title`, keyword search joins
`experiences_fts` to the base `experiences` table on `rowid` (`e.rowid = experiences_fts.rowid`)
— the view carries no rowid, and the FTS table is intrinsically bound to `experiences`. This is
the one read that touches a base table rather than a `v_*` view, and it is read-only.

## Write contract — allowed tables + invariants

Writes touch raw tables (there is no writable view). `foreign_keys = ON` is mandatory. Entity
kinds: `person | team | project | org | tool | other`.

| Table | Write rule |
|---|---|
| `entities` | `INSERT OR IGNORE (id, kind, name)` then `SELECT id WHERE kind=? AND name=?`. Natural key `(kind, name)` is `UNIQUE`. |
| `edges` | `INSERT OR IGNORE (id, src_kind, src_id, rel, dst_kind, dst_id)`. Natural key `(src_kind, src_id, rel, dst_kind, dst_id)` is `UNIQUE`. Endpoints must exist. |
| `experiences` | `INSERT (id, title, situation, task, action, result_text, context, status, created_at, updated_at)`. `status ∈ {draft, confirmed}`; default new rows to `draft`. `experiences_fts` updates automatically via triggers. |
| `experience_skills` / `experience_tags` / `experience_metrics` | Link rows for a captured experience. Upsert `skills`/`tags` by their `UNIQUE name` first. |
| `embed_queue` | After inserting an experience, `INSERT OR IGNORE (experience_id, enqueued_at)`. STARfolio's drainer embeds it on next launch; until then the row is fully usable except in semantic search. |

**Never written by PersonalServer:** `vec_experiences`, `vec_corpus` (vector tables — STARfolio's
embed pipeline owns them), any `*_fts` table (trigger-maintained), `schema_migrations`,
`usage_log`, `settings`.

## Knowledge-graph model exposed over MCP

- **Nodes** = experiences (`v_experiences`) ∪ entities (`v_entities`).
- **Edges** = stored `v_edges` (explicit relations, e.g. `mentions`) plus two relations
  PersonalServer derives in SQL and does not store: **shared-entity** and **shared-skill** links
  between experiences (the 1–2-hop traversal STARfolio's `neighborsOf` implements).

## AI bridge (Stage 3 — requires STARfolio running)

Semantic (vector-KNN) retrieval and grounded story generation stay in STARfolio. When an MCP
client needs them, PersonalServer calls a loopback HTTP API STARfolio exposes on `127.0.0.1`
(port/token written to a file under `userData`; read, never guessed). Unreachable ⇒ tools return
`{ error: "starfolio_not_running" }` and the client falls back to the read tools. Details land
with Stage 3.
