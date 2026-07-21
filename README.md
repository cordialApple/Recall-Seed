# PersonalServer

**A grounded career-experience harness, delivered as an MCP server.** It turns a plain
Obsidian-style markdown vault of your work into JD-tailored resume bullets, spoken interview answers,
cross-vault tendencies, and an evidence-weighted expertise profile — and its whole reason to exist is
that it **never fabricates**. Every metric is verbatim from your notes, every claim traces to a source
id, and it will *refuse to vouch a thin story rather than invent one*.

Written in C# on **.NET 10** using the official
[`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) SDK; built for
[Model Context Protocol](https://modelcontextprotocol.io/) clients like **Claude Desktop**.

> **See [`docs/PROOF.md`](docs/PROOF.md)** — redacted real runs against a live vault: a grounded STAR,
> resume bullets each tagged with their source `experience_id`, real `find_tendencies` counts, and the
> keystone — a thin note that yields a gap question, **not an invented metric**.

## The moat: grounding + ensureCited

Two disciplines, embedded verbatim in [`Vault/Prompts.cs`](Vault/Prompts.cs), make the output
trustworthy:

- **Grounding** — never invent a fact, number, or outcome. An absent beat stays thin, low-confidence,
  and carries a *gap question*; metrics are copied verbatim. Vouching a note as true
  (`confirm_experience`) is refused while its action/result is thin or open gaps remain — and
  `write_experience` won't mint a `confirmed` note either, so confirmed status is only ever earned
  through that gate.
- **ensureCited** — every *non-terminal* interview question must cite ≥1 real corpus `chunk_id`; a
  question that can't be tied to one is out of bounds. `check_citation` is the guard that verifies the
  cited ids are real before the question is asked.

The LLM runs in the *calling* agent; each tool assembles the grounded prompt plus the real
corpus/aggregates and hands them back. The server retrieves and captures — it does not author.

## Lineage: the STARfolio successor

PersonalServer is the durable core of **STARfolio**, a private single-user Electron desktop app for
banking accomplishments in STAR form (Situation/Task/Action/Result). STARfolio's realtime surface —
live voice interview, ASR, token-streaming — is app-only and stays there. Its *portable* IP, the
grounded prompts and the defend-your-own-repo interview, was extracted and coalesced into this MCP
server over a plain markdown vault, so the moat lives in code you can read rather than a frozen app.
STARfolio is now a **sibling to complement, not a backend**.

## The vault is the substrate

One experience = one markdown file under `experiences/`; `[[wikilinks]]` + backlinks are the knowledge
graph for free. No database. The vault location resolves from the `EXPERIENCE_VAULT` environment
variable, else `~/Documents/Design_Exp`. A missing vault returns a structured error naming the resolved
path, not a crash. See [`Vault/README.md`](Vault/README.md) for the full rationale.

> **How an MCP stdio server runs:** it is *not* a daemon and you never start it yourself. The client
> (e.g. Claude Desktop) launches the server as a child process on demand and talks to it over stdio.
> `stdout` is reserved for the JSON-RPC protocol; **all logging goes to stderr**. "Always available"
> just means the client can spawn it instantly — which is why we publish a fast, self-contained binary
> and point the client at it (no `dotnet run`, no SDK needed at launch).

## Tools

| tool | does |
|---|---|
| `ping` | health check — echoes a message + UTC time |
| `bank_experience` | grounded extractor (notes/resume/evidence) + entity prompt + STAR schema + known entities |
| `write_experience` | commit a grounded STAR note to `experiences/`, metrics verbatim, entities as `[[wikilinks]]` (creates drafts) |
| `update_experience` | patch a note by id; only the fields you pass change. answer a gap by filling its beat and passing the trimmed gaps list |
| `confirm_experience` | flip `draft -> confirmed` by id; refuses and reports why if action/result is thin or gaps remain |
| `search_experiences` | free-text search over the vault, ranked, with snippets |
| `query_experiences` | structured filter by skills / tags / context / status |
| `get_experience` | fetch one full note by id |
| `neighbors` | experiences connected via shared entity or skill |
| `tailor_bullets` | vault blocks + BULLETS prompt → JD-tailored resume bullets, grounded per id |
| `generate_story` | named experiences + behavioral-answer prompt + voice → a spoken interview answer, grounded to their ids |
| `debrief_interview` | interview transcript (as DATA) + debrief prompt + optional experiences → grounded feedback, strengths, gaps, reconstructed STAR |
| `find_tendencies` | real cross-vault aggregates + tendency-analysis prompt |
| `expertise_profile` | skills weighted by evidence count, metric-carrying results, domains |
| `defend_repo` | own experiences as cited corpus chunks + interviewer prompt + ensureCited |
| `check_citation` | hard guard — are these chunk_ids real? |
| `voice_guide` | voice rules + recent notes as fresh style samples |

Tools are auto-discovered via `[McpServerToolType]` + `WithToolsFromAssembly()`, so adding one is just
a new annotated class — no `Program.cs` change.

## Layout

```
PersonalServer.csproj             # net10.0 console app, self-contained single-file publish
Program.cs                        # Generic Host: stdio transport + WithToolsFromAssembly()
Tools/PingTools.cs                # the `ping` health-check tool
Vault/                            # the career harness — prompts, vault store, and all tools
claude_desktop_config.sample.json # reference snippet for an MCP client's config
scripts/smoke-ping.ps1            # non-interactive end-to-end smoke test
tests/PersonalServer.Tests/       # xunit tests over a temp vault
docs/PROOF.md                     # redacted real runs (the proof)
```

## Build & publish

Published as a **self-contained, single-file binary** (no .NET runtime required on the target). The
csproj declares runtime identifiers for Windows, macOS, and Linux
(`win-x64; win-arm64; osx-arm64; linux-x64; linux-arm64; linux-musl-x64`) — publish for yours:

```bash
# Windows (PowerShell)
dotnet publish -c Release -r win-x64   -o "$env:LOCALAPPDATA\PersonalServer"
# macOS (Apple silicon)
dotnet publish -c Release -r osx-arm64 -o ~/.local/share/PersonalServer
# Linux
dotnet publish -c Release -r linux-x64 -o ~/.local/share/PersonalServer
```

This drops a single `PersonalServer` binary (`.exe` on Windows) at a stable location outside the repo,
so the artifact survives `dotnet clean` and repo moves. Re-run after **any** code change, then fully
restart your MCP client — the built binary is a snapshot, not live source.

## Use it from an MCP client (e.g. Claude Desktop)

1. **Publish** the binary (above).
2. **Point the client's MCP config at it.** Claude Desktop's config lives at:

   ```
   Windows (standalone):        %APPDATA%\Claude\claude_desktop_config.json
   Windows (Microsoft Store):   %LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json
   macOS:                       ~/Library/Application Support/Claude/claude_desktop_config.json
   ```

   Add the `PersonalServer` entry (see [`claude_desktop_config.sample.json`](claude_desktop_config.sample.json)).
   Use the **full literal path** to the binary if your client doesn't expand environment variables. Set
   `EXPERIENCE_VAULT` in its `env` if your vault isn't at `~/Documents/Design_Exp`:

   ```json
   {
     "mcpServers": {
       "PersonalServer": {
         "command": "/absolute/path/to/PersonalServer",
         "env": { "EXPERIENCE_VAULT": "/absolute/path/to/your/vault" }
       }
     }
   }
   ```

3. **Fully restart the client** so it reloads the config and spawns the server (on a tray app, quit
   from the tray — closing the window isn't enough).
4. **Verify:** confirm the tools are listed, then try `find_tendencies` — it reports real counts off
   your vault.

> **Windows Store (MSIX) build of Claude Desktop:** packaged apps virtualize AppData, so they do **not**
> read `%APPDATA%\Claude\`. Edit the `LocalCache` path above, and quit Claude Desktop *completely* first
> (tray → Quit) — the packaged app rewrites its config on preference changes and will clobber edits made
> while it's running. Troubleshooting logs live next to the config: `…\Roaming\Claude\logs\mcp-server-PersonalServer.log`.

## Other ways to run / test

- **Smoke test (no client needed):**
  ```powershell
  powershell -File scripts/smoke-ping.ps1
  ```
  Builds the project, drives the server over stdio (`initialize` → `tools/list` → `tools/call ping`),
  prints the raw JSON-RPC, and asserts `ping`/`pong`.

- **Full test suite:**
  ```bash
  dotnet test tests/PersonalServer.Tests
  ```

- **MCP Inspector (interactive):**
  ```bash
  npx @modelcontextprotocol/inspector /path/to/PersonalServer
  ```

## License

[MIT](LICENSE).
