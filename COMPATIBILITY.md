# Compatibility with STARfolio

Recall Seed is the vault-native successor to STARfolio's experience bank. Both model the same
thing (a STAR experience with skills, tags, metrics, entities, and a draft/confirmed lifecycle), so
a note here maps cleanly onto a STARfolio `experiences` row. This file records that mapping and the
deltas, so the two stay interoperable.

- **Recall Seed**: one markdown note per experience under `experiences/`, frontmatter + `##` beats.
  Entities are `[[wikilinks]]`; backlinks are the graph. No database.
- **STARfolio**: SQLite (`superstar.db`), `experiences` table + joined `skills`/`tags`/`metrics`/
  `sources`, plus `entities` + `edges` for the graph.

## Field map

| Concept | Recall Seed note | STARfolio row | Match |
|---|---|---|---|
| id | `id` (stable slug) | `id` | same |
| title | `title` | `title` | same |
| situation / task / action | `## Situation` / `## Task` / `## Action` beats | `situation` / `task` / `action` | same |
| result | `## Result` beat (`Experience.Result`) | `result_text` | same concept, **name differs** |
| context | `context: work\|project\|class\|other` | `context` (same enum) | same |
| status | `status: draft\|confirmed` | `status: draft\|confirmed` | same |
| per-beat confidence | frontmatter `confidence: {beat: high\|medium\|low}` | extraction confidence in `draft_state_json` | same values, **stored differently** |
| skills | `skills: [{name, kind: technical\|soft\|domain}]` | joined `skills(name, kind)` | same |
| tags | `tags: [string]` | joined `tags` | same |
| metrics | `metrics: [{label, value?, unit?}]` | joined `metrics(label, value, unit)` | same |
| entities | `entities: [[[wikilink]]]` (names only) + backlinks | `entities(kind, name)` + `edges` | **graph is emergent here (no edges table); entity kind is prompt-level convention, not stored** |
| story/answer framing | `generate_story` `kind: jd\|genre` (A1) | story prompt `kind: jd\|genre` (nested in `prompt_json`, not a `stories` column) | same knob |
| provenance | bullets/story cite experience `id` | `stories.experience_ids_json` | same at the id level |

## Deltas (not blocking)

- **`result` vs `result_text`**: same R beat, different field name. No code change; just know the alias.
- **Confidence storage**: Recall Seed persists per-beat confidence in frontmatter; STARfolio keeps
  it in `draft_state_json`. Same high/medium/low semantics.
- **Dates**: STARfolio has `happened_start` / `happened_end`; the note has none (the id slug is usually
  date-prefixed, and file mtime gives `UpdatedUtc`). Could add `happened_*` to frontmatter later.
- **Sources chain**: STARfolio copies source attachments (`sources` / `experience_sources`,
  content-hash named); the vault has no attachment store. Provenance here is the note itself plus its
  gap questions.
- **Graph**: STARfolio uses an explicit `edges` table; the vault derives the graph from `[[wikilinks]]`
  and backlinks. Entity kind is a prompt-level convention here; STARfolio stores it explicitly.

## Discipline (identical on both sides)

Grounding and citation are the shared moat: never invent a fact, number, employer, or outcome; keep
metrics verbatim; an absent beat stays thin + low-confidence + a gap question, never self-filled;
every interview question cites a real corpus chunk. See `Vault/Prompts.cs` for the enforced rules.
