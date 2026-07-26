# The DB contract (sqlite backend)

Part of the [architecture](../architecture.md).

The `sqlite` backend reads and writes STARfolio's `superstar.db`. It couples to a stable contract,
not to raw tables: (1) a set of read-only `v_*` views STARfolio keeps shaped as described across its
own migrations, (2) the concurrency pragmas both processes set, and (3) the few raw tables writes may
touch, with their invariants. This backend is opt-in; the default backend is the markdown vault, which
has no database. See [COMPATIBILITY.md](../../COMPATIBILITY.md) for the field map.

## Finding the database

Resolution order:

1. `SUPERSTAR_DB_PATH`, else
2. the platform default: `%APPDATA%\STARfolio\superstar.db` on Windows (the file is `superstar.db`
   under STARfolio's Electron `userData`).

If the file does not exist, tools return a structured "database not found" error naming the resolved
path. They never throw a raw exception over stdio.

## Concurrency

STARfolio runs the database in WAL mode with `foreign_keys = ON`. The sqlite backend matches that on
its side:

- `Pooling=False`, WAL inherited from the file, and `PRAGMA busy_timeout = 5000` plus
  `PRAGMA foreign_keys = ON` on every connection.
- Reads open logically read-only via `Mode=ReadWrite` plus `PRAGMA query_only = ON`. A WAL database
  needs shared-memory write access to read while STARfolio holds it open, so a file-level
  `Mode=ReadOnly` handle can fail mid-read. `query_only` gives logical read-only while SQLite manages
  the WAL index.
- Writes open read-write, do their work in a short transaction, and close promptly. No long-held locks.

WAL permits many readers plus one writer. With two possible writers (STARfolio and Recall Seed),
`busy_timeout` serializes them. Personal write frequency makes contention negligible. Keep every write
transaction small.

## Read contract: views

The read tools query only these views. STARfolio owns their DDL and keeps the listed columns stable.
Underlying tables may be refactored freely behind them.

- `v_experiences`: `id, title, situation, task, action, result_text, context, status, happened_start,
  happened_end, created_at, updated_at`.
- `v_experience_skills`: `experience_id, skill_name, skill_kind`.
- `v_experience_tags`: `experience_id, tag_name`.
- `v_experience_metrics`: `experience_id, label, value, unit`.
- `v_entities`: `id, kind, name`, where `kind` is one of person, team, project, org, tool, other.
- `v_edges`: `src_kind, src_id, rel, dst_kind, dst_id`.
- `v_experience_sources`: `experience_id, source_kind, uri_or_path, title` (provenance, read-only).

Keyword search does not go through a view. FTS5 `MATCH` references the real `experiences_fts` table
(external-content over title, situation, task, action, result_text, kept in sync by triggers), which
is a named, stable part of the contract. Query building: tokenize input, quote tokens, append `*` to
the last token, and `ORDER BY rank` ascending so the best match is first.

## Write contract: allowed tables

Writes touch raw tables directly, so each path preserves its own invariants. `foreign_keys = ON` is
mandatory on the connection.

- `entities`: `INSERT` needs `id` (UUID), `kind` (one of the six), `name`. De-dup on `(kind, name)`
  with `INSERT OR IGNORE`, then `SELECT id`.
- `edges`: `INSERT` needs `id` (UUID) and `src_kind, src_id, rel, dst_kind, dst_id`. Referenced ids
  must exist. `INSERT OR IGNORE` for idempotency on the natural key.
- `experiences`: `INSERT` needs `id` (UUID), `title`, at least one STAR field, `context`, `status`
  (`draft` or `confirmed`), and `created_at`/`updated_at`. The `experiences_fts` sync triggers fire
  automatically, so keyword search sees the row with no extra step.
- `embed_queue`: after inserting an experience, `INSERT OR IGNORE INTO embed_queue (experience_id,
  enqueued_at)`. STARfolio's background drainer embeds queued rows on its next launch. Until then the
  row is fully usable for keyword, structured, and graph queries, and is simply absent from semantic
  results. This is intended graceful degradation. Do not embed from C#.

Writes must not touch `vec_experiences` or `vec_corpus` (vector tables, STARfolio-owned via the embed
pipeline), `schema_migrations`, `usage_log`, or any `*_fts` table (trigger-maintained).

## Graph model

Nodes are experiences plus entities. Edges are the `edges` table (explicit relations such as
`mentions`) plus two relations computed in SQL, not stored: shared-entity and shared-skill links
between experiences, a 1 to 2 hop traversal. This is the same graph the vault backend derives from
`[[wikilinks]]` and backlinks, so `neighbors` behaves identically on both stores.
</content>
