# Architecture

Recall Seed is a stdio MCP server on .NET 10. A client (Claude Desktop) spawns it as a child
process and talks JSON-RPC over stdio. `stdout` carries the protocol; all logging goes to `stderr`.
Tool classes are auto-discovered via `[McpServerToolType]` + `WithToolsFromAssembly()`, so adding a
tool is a new annotated class with no `Program.cs` wiring.

The design has three parts: a store seam with two backends, a grounding layer that sits above the
seam, and a Scroll endpoint seam for seeding coding challenges.

## Store seam: two backends

Every tool talks to [`IExperienceStore`](../Vault/IExperienceStore.cs), never to a concrete store.
Two implementations sit behind it:

- **vault** (default): one experience is one markdown file under `experiences/`. `[[wikilinks]]` plus
  backlinks are the knowledge graph. No database. Location resolves from `EXPERIENCE_VAULT`, else
  `~/Documents/Design_Exp`.
- **sqlite** (opt-in): reads and writes STARfolio's `superstar.db` directly, over a stable views and
  write contract. Full read/write shape and concurrency rules in
  [architecture/db-contract.md](architecture/db-contract.md).

[`BackendConfig`](../Vault/BackendConfig.cs) picks the store. Precedence: env vars
(`EXPERIENCE_BACKEND`, `EXPERIENCE_VAULT`, `SUPERSTAR_DB_PATH`) override a STARfolio-written config
file, which overrides the built-in default of `vault`. A `superstar.db` present but not chosen stays
`vault`. sqlite is opt-in, never silently assumed. The `Resolve` logic is pure, so precedence is unit
tested without env or filesystem.

## Grounding sits above the seam

The moat is backend-agnostic. Both stores get the same discipline, embedded verbatim in
[`Vault/Prompts.cs`](../Vault/Prompts.cs):

- **Grounding**: never invent a fact, number, employer, or outcome. An absent beat stays thin,
  low-confidence, and carries a gap question. Metrics are copied verbatim. `confirm_experience` is the
  only path to `confirmed` status, and it refuses while the action or result is thin or gaps remain.
- **ensureCited**: every non-terminal interview question cites at least one real corpus `chunk_id`.
  `check_citation` verifies the cited ids are real before the question is asked.

The LLM runs in the calling agent. Each tool assembles a grounded prompt plus real corpus and
aggregates and hands it back. The server retrieves and captures. It does not author.

Field-level parity with STARfolio's schema is in [COMPATIBILITY.md](../COMPATIBILITY.md). Redacted
real runs are in [PROOF.md](PROOF.md).

## Scroll endpoint seam

The [`Scroll/`](../Scroll) tools are Recall Seed's side of the endpoint-spawner contract with Scroll,
a sibling editor. Recall Seed hosts no editor and no grader:

- `seed_ide_endpoint` builds and validates an `ide-es` schema (problem, function name, hidden tests, a
  per-test TLE budget, optional staged hints), bakes in a correlation-bearing loopback callback URL,
  and returns a spawn URL. The user opens it; Scroll spawns the editor and runs the hidden-test grader.
- `get_scroll_verdict` reads the grading verdict Scroll POSTs back, keyed by the `spawnId` from
  `seed_ide_endpoint`.

The verdict sink is a loopback receiver on `http://127.0.0.1:{SCROLL_VERDICT_PORT}/`. It is off by
default, so the process stays a pure stdio server with no listening socket unless `SCROLL_VERDICT_PORT`
is set. It answers a CORS preflight, strict-parses a small JSON POST, and logs it by correlation id. A
handler fault never takes down the accept loop.

## Boundaries

- **No secrets.** Recall Seed holds no API key. The vault backend is pure local file I/O. The sqlite
  backend reads and writes a local database file. Neither does network egress of its own.
- **Structured errors, not exceptions.** Tools return result objects. Expected failures (vault
  missing, id not found, database not found) come back with an `error` field and a readable message,
  never a raw throw over stdio.
- **stderr only.** `Program.cs` routes all logging to stderr so stdout stays a clean protocol channel.
</content>
