# Recall Seed

[![CI](https://github.com/cordialApple/Recall-Seed/actions/workflows/ci.yml/badge.svg)](https://github.com/cordialApple/Recall-Seed/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-stdio%20server-black.svg)](https://modelcontextprotocol.io/)

**A grounded career-experience harness, delivered as an MCP server.** It turns a plain
Obsidian-style markdown vault of your work into JD-tailored resume bullets, spoken interview answers,
cross-vault tendencies, and an evidence-weighted expertise profile, and its whole reason to exist is
that it **never fabricates**. Every metric is verbatim from your notes, every claim traces to a source
id, and it will *refuse to vouch a thin story rather than invent one*.

Written in C# on **.NET 10** using the official
[`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) SDK; built for
[Model Context Protocol](https://modelcontextprotocol.io/) clients like **Claude Desktop**.

> **Status:** working. All 19 tools are callable over stdio; CI builds Release, runs the xunit suite,
> and drives an end-to-end stdio smoke (`initialize` → `tools/list` → `tools/call ping`) on every push.
> The markdown **vault** backend is the default and dogfooded against a real vault; the STARfolio
> **SQLite** backend and the **Scroll** endpoint seam are implemented and covered by tests.

> **See [`docs/PROOF.md`](docs/PROOF.md)**, redacted real runs against a live vault: a grounded STAR,
> resume bullets each tagged with their source `experience_id`, real `find_tendencies` counts, and the
> keystone, a thin note that yields a gap question, **not an invented metric**.

## Tools

**Career vault**, the grounded harness over your experiences:

| tool | does |
|---|---|
| `ping` | health check, echoes a message + UTC time |
| `bank_experience` | grounded extractor (notes/resume/evidence) + entity prompt + STAR schema + known entities |
| `write_experience` | commit a grounded STAR note, metrics verbatim, entities as `[[wikilinks]]` (creates drafts) |
| `update_experience` | patch a note by id; only the fields you pass change. answer a gap by filling its beat and trimming the gaps list |
| `confirm_experience` | flip `draft -> confirmed` by id; refuses and reports why if action/result is thin or gaps remain |
| `search_experiences` | free-text search over the store, ranked, with snippets |
| `query_experiences` | structured filter by skills / tags / context / status |
| `get_experience` | fetch one full note by id |
| `neighbors` | experiences connected via shared entity or skill |
| `tailor_bullets` | vault blocks + BULLETS prompt → JD-tailored resume bullets, grounded per id |
| `generate_story` | named experiences + behavioral-answer prompt + voice → a spoken interview answer, grounded to their ids |
| `debrief_interview` | interview transcript (as DATA) + debrief prompt + optional experiences → grounded feedback, strengths, gaps, reconstructed STAR |
| `find_tendencies` | real cross-vault aggregates + tendency-analysis prompt |
| `expertise_profile` | skills weighted by evidence count, metric-carrying results, domains |
| `defend_repo` | own experiences as cited corpus chunks + interviewer prompt + ensureCited |
| `check_citation` | hard guard, are these chunk_ids real? |
| `voice_guide` | voice rules + recent notes as fresh style samples |

**Scroll endpoints**, seed a coding challenge into the sibling **Scroll** editor and read the verdict:

| tool | does |
|---|---|
| `seed_ide_endpoint` | author a hidden-test coding problem, get back a Scroll spawn URL to hand the user (Scroll hosts the editor + runs the grader) |
| `get_scroll_verdict` | read the pass/fail/tle grading verdict Scroll POSTs back, keyed by the `spawnId` from `seed_ide_endpoint` |

Tools are auto-discovered via `[McpServerToolType]` + `WithToolsFromAssembly()`, so adding one is just
a new annotated class, no `Program.cs` change.

## Scroll: seed a coding endpoint, read the verdict

The Scroll tools are Recall Seed's side of the endpoint-spawner contract with **Scroll**, a
sibling collaborative editor. Recall Seed does **not** host an editor or a grader: `seed_ide_endpoint`
builds and validates an `ide-es` schema (problem, function name, hidden tests, a per-test TLE budget,
optional staged hints), bakes in a correlation-bearing loopback callback URL, and hands back a spawn
URL. You give that URL to the user; Scroll spawns the editor and runs the hidden-test grader.

When the user submits, Scroll POSTs the grading verdict back to a **loopback receiver** the server
runs on `http://127.0.0.1:{SCROLL_VERDICT_PORT}/`. That receiver is **off by default** (a bare stdio
MCP server with no listening socket) and only binds when `SCROLL_VERDICT_PORT` is set. It accepts a
small JSON POST (with a CORS preflight answered so the browser fetch reaches it), strict-parses it, and
logs it keyed by the correlation id. `get_scroll_verdict(spawnId)` then reads the latest verdict for
that problem. If the receiver is disabled, `seed_ide_endpoint` reports `callbackWired: false` and no
verdict will ever arrive.

## Lineage: the STARfolio successor

Recall Seed is the durable core of **STARfolio**, a private single-user Electron desktop app for
banking accomplishments in STAR form (Situation/Task/Action/Result). STARfolio's realtime surface
(live voice interview, ASR, token-streaming) is app-only and stays there. Its *portable* IP, the
grounded prompts and the defend-your-own-repo interview, was extracted and coalesced into this MCP
server over a plain markdown vault, so the moat lives in code you can read rather than a frozen app.
STARfolio is now a **sibling to complement, not a backend**. The field-level mapping between the two
lives in [`COMPATIBILITY.md`](COMPATIBILITY.md).

## Storage: one seam, two backends

The default backend is the **vault**: one experience = one markdown file under `experiences/`;
`[[wikilinks]]` + backlinks are the knowledge graph for free, no database. The vault location resolves
from `EXPERIENCE_VAULT`, else `~/Documents/Design_Exp`. A missing vault returns a structured error
naming the resolved path, not a crash. See [`Vault/README.md`](Vault/README.md) for the full rationale.

Behind [`IExperienceStore`](Vault/IExperienceStore.cs) sits a second, opt-in backend: **sqlite**, which
reads and writes STARfolio's own `superstar.db` over a stable views/contract surface (WAL mode, so it
works whether or not the STARfolio GUI is open). [`BackendConfig`](Vault/BackendConfig.cs) picks the
store: env vars override a STARfolio-written config file, which overrides the built-in vault default:

```
EXPERIENCE_BACKEND = vault | sqlite       # default: vault
EXPERIENCE_VAULT   = /path/to/your/vault  # vault backend
SUPERSTAR_DB_PATH  = /path/to/superstar.db # sqlite backend
```

Same tools, same grounding, either substrate. The moat sits above the seam, so it is identical on
both stores. The sqlite backend opens the DB in WAL mode with a `busy_timeout`, so it reads and writes
concurrently whether or not the STARfolio GUI is open, and only ever through STARfolio's stable
read/write contract surface. The full shape is in
[`docs/architecture/db-contract.md`](docs/architecture/db-contract.md); the design overview is in
[`docs/architecture.md`](docs/architecture.md).

> **How an MCP stdio server runs:** it is *not* a daemon and you never start it yourself. The client
> (e.g. Claude Desktop) launches the server as a child process on demand and talks to it over stdio.
> `stdout` is reserved for the JSON-RPC protocol; **all logging goes to stderr**. "Always available"
> just means the client can spawn it instantly, which is why we publish a fast, self-contained binary
> and point the client at it (no `dotnet run`, no SDK needed at launch).

## The moat: grounding + ensureCited

Two disciplines, embedded verbatim in [`Vault/Prompts.cs`](Vault/Prompts.cs), make the output
trustworthy:

- **Grounding**: never invent a fact, number, or outcome. An absent beat stays thin, low-confidence,
  and carries a *gap question*; metrics are copied verbatim. Vouching a note as true
  (`confirm_experience`) is refused while its action/result is thin or open gaps remain, and
  `write_experience` won't mint a `confirmed` note either, so confirmed status is only ever earned
  through that gate.
- **ensureCited**: every *non-terminal* interview question must cite ≥1 real corpus `chunk_id`; a
  question that can't be tied to one is out of bounds. `check_citation` is the guard that verifies the
  cited ids are real before the question is asked.

The LLM runs in the *calling* agent; each tool assembles the grounded prompt plus the real
corpus/aggregates and hands them back. The server retrieves and captures. It does not author.

## Environment variables

All optional. The defaults give a working vault-backed server with the Scroll receiver off.

| variable | default | purpose |
|---|---|---|
| `EXPERIENCE_BACKEND` | `vault` | `vault` or `sqlite`, which store to run against |
| `EXPERIENCE_VAULT` | `~/Documents/Design_Exp` | vault-backend markdown root |
| `SUPERSTAR_DB_PATH` | STARfolio's `%APPDATA%/STARfolio/superstar.db` | sqlite-backend database path |
| `PERSONALSERVER_CONFIG_FILE` | per-OS `…/Recall_Seed/config.json` | override the STARfolio-written backend config file |
| `SCROLL_BASE_URL` | `http://localhost:5173` | where Scroll is hosted (the spawn URL origin) |
| `SCROLL_VERDICT_PORT` | *(unset → receiver off)* | loopback port for Scroll to POST grading verdicts back |

## Layout

```
Recall_Seed.csproj                # net10.0 console app, self-contained single-file publish
Program.cs                        # Generic Host: stdio transport, WithToolsFromAssembly(), backend + Scroll receiver
Tools/PingTools.cs                # the `ping` health-check tool
Vault/                            # the career harness, prompts, store seam (vault + sqlite), and all tools
Data/                             # STARfolio SQLite access (Db.cs, DbGuard.cs) for the sqlite backend
Scroll/                           # endpoint-spawner contract, seed tool, verdict receiver + tools
claude_desktop_config.sample.json # reference snippet for an MCP client's config
COMPATIBILITY.md                  # field-level mapping to STARfolio's schema
scripts/smoke-ping.ps1            # non-interactive end-to-end smoke test
tests/Recall_Seed.Tests/          # xunit tests over a temp vault + sqlite + Scroll receiver
docs/PROOF.md                     # redacted real runs (the proof)
```

Internal planning and architecture notes live under `docs/` and are kept local (gitignored), only
`docs/PROOF.md` ships.

## Build & publish

Published as a **self-contained, single-file binary** (no .NET runtime required on the target). The
csproj declares runtime identifiers for Windows, macOS, and Linux
(`win-x64; win-arm64; osx-arm64; linux-x64; linux-arm64; linux-musl-x64`), publish for yours:

```bash
# Windows (PowerShell)
dotnet publish -c Release -r win-x64   -o "$env:LOCALAPPDATA\Recall_Seed"
# macOS (Apple silicon)
dotnet publish -c Release -r osx-arm64 -o ~/.local/share/Recall_Seed
# Linux
dotnet publish -c Release -r linux-x64 -o ~/.local/share/Recall_Seed
```

This drops a single `Recall_Seed` binary (`.exe` on Windows) at a stable location outside the repo,
so the artifact survives `dotnet clean` and repo moves. Re-run after **any** code change, then fully
restart your MCP client. The built binary is a snapshot, not live source.

## Use it from an MCP client (e.g. Claude Desktop)

1. **Publish** the binary (above).
2. **Point the client's MCP config at it.** Claude Desktop's config lives at:

   ```
   Windows (standalone):        %APPDATA%\Claude\claude_desktop_config.json
   Windows (Microsoft Store):   %LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json
   macOS:                       ~/Library/Application Support/Claude/claude_desktop_config.json
   ```

   Add the `Recall_Seed` entry (see [`claude_desktop_config.sample.json`](claude_desktop_config.sample.json)).
   Use the **full literal path** to the binary if your client doesn't expand environment variables. Set
   `EXPERIENCE_VAULT` in its `env` if your vault isn't at `~/Documents/Design_Exp` (or `EXPERIENCE_BACKEND`
   / `SUPERSTAR_DB_PATH` to run against STARfolio's database instead):

   ```json
   {
     "mcpServers": {
       "Recall_Seed": {
         "command": "/absolute/path/to/Recall_Seed",
         "env": { "EXPERIENCE_VAULT": "/absolute/path/to/your/vault" }
       }
     }
   }
   ```

3. **Fully restart the client** so it reloads the config and spawns the server (on a tray app, quit
   from the tray; closing the window isn't enough).
4. **Verify:** confirm the tools are listed, then try `find_tendencies`, it reports real counts off
   your vault.

> **Windows Store (MSIX) build of Claude Desktop:** packaged apps virtualize AppData, so they do **not**
> read `%APPDATA%\Claude\`. Edit the `LocalCache` path above, and quit Claude Desktop *completely* first
> (tray → Quit). The packaged app rewrites its config on preference changes and will clobber edits made
> while it's running. Troubleshooting logs live next to the config: `…\Roaming\Claude\logs\mcp-server-Recall_Seed.log`.

## Other ways to run / test

- **Smoke test (no client needed):**
  ```powershell
  powershell -File scripts/smoke-ping.ps1
  ```
  Builds the project, drives the server over stdio (`initialize` → `tools/list` → `tools/call ping`),
  prints the raw JSON-RPC, and asserts `ping`/`pong`.

- **Full test suite:**
  ```bash
  dotnet test tests/Recall_Seed.Tests
  ```

- **MCP Inspector (interactive):**
  ```bash
  npx @modelcontextprotocol/inspector /path/to/Recall_Seed
  ```

## License

[MIT](LICENSE).
