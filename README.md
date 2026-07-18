# PersonalServer

A [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server written in C# on
**.NET 10**, using the official [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol)
SDK. It is a **career-experience harness**: tools over an Obsidian-style markdown vault that bank
grounded STAR notes, tailor resume bullets, surface tendencies in how you work, profile your
provable expertise, and run a defend-your-own-repo mock interview. Built for the **Claude Desktop**
client.

It speaks **stdio only** — a Generic Host app that exchanges JSON-RPC over stdin/stdout. `stdout`
is reserved for the protocol; **all logging goes to stderr**.

> **How an MCP stdio server runs:** it is *not* a daemon and you never start it yourself. The
> client (Claude Desktop) launches the server as a child process on demand and talks to it over
> stdio. So "always available" just means the client can spawn it instantly — which is why we
> publish a fast, self-contained `.exe` and point Claude Desktop at it (no `dotnet run`, no SDK
> needed at launch). A Windows service is *not* used: a detached service has no stdio pipe for a
> client to attach to.

## The vault is the substrate

One experience = one markdown file under `experiences/`; `[[wikilinks]]` + backlinks are the
knowledge graph for free. No database. The vault location is the `EXPERIENCE_VAULT` env var, else
`~/Documents/Design_Exp`. Two disciplines make the output trustworthy and live verbatim in
`Vault/Prompts.cs`: **grounding** (never invent a fact/number/outcome; absent beats stay thin +
low-confidence + a gap question; metrics verbatim) and **ensureCited** (every interview question
cites a real corpus chunk — enforced by `check_citation`). The LLM runs in the calling agent; each
tool assembles the grounded prompt + the real corpus/aggregates and hands them back.

See [`Vault/README.md`](Vault/README.md) for the full rationale.

## Tools

| tool | does |
|---|---|
| `ping` | health check — echoes a message + UTC time |
| `bank_experience` | grounded extractor (notes/resume/evidence) + entity prompt + STAR schema + known entities |
| `write_experience` | commit a grounded STAR note to `experiences/`, metrics verbatim, entities as `[[wikilinks]]` |
| `update_experience` | patch a note by id; only the fields you pass change. answer a gap by filling its beat and passing the trimmed gaps list |
| `confirm_experience` | flip `draft -> confirmed` by id; refuses and reports why if action/result is thin or gaps remain |
| `search_experiences` | free-text search over the vault, ranked, with snippets |
| `query_experiences` | structured filter by skills / tags / context / status |
| `get_experience` | fetch one full note by id |
| `neighbors` | experiences connected via shared entity or skill |
| `tailor_bullets` | vault blocks + BULLETS prompt → JD-tailored resume bullets, grounded per id |
| `generate_story` | named experiences + behavioral-answer prompt + voice → a spoken interview answer, grounded to their ids |
| `find_tendencies` | real cross-vault aggregates + tendency-analysis prompt |
| `expertise_profile` | skills weighted by evidence count, metric-carrying results, domains |
| `defend_repo` | own experiences as cited corpus chunks + interviewer prompt + ensureCited |
| `check_citation` | hard guard — are these chunk_ids real? |
| `voice_guide` | voice rules + recent notes as fresh style samples |

Tools are auto-discovered via `[McpServerToolType]` + `WithToolsFromAssembly()`, so adding one is
just a new annotated class — no `Program.cs` change.

## Layout

```
PersonalServer.csproj             # net10.0 console app, self-contained single-file publish
Program.cs                        # Generic Host: stdio transport + WithToolsFromAssembly()
Tools/PingTools.cs                # the `ping` health-check tool
Vault/                            # the career harness — prompts, vault store, and all tools
claude_desktop_config.sample.json # reference snippet for Claude Desktop's config
scripts/smoke-ping.ps1            # non-interactive end-to-end smoke test
tests/PersonalServer.Tests/       # xunit tests over a temp vault
```

## Build & publish

Published as a **self-contained, single-file exe** (no .NET runtime required on the target) to a
stable location outside the repo, so the artifact survives `dotnet clean` and repo moves:

```powershell
dotnet publish -c Release -r win-x64 -o "$env:LOCALAPPDATA\PersonalServer"
# -> %LOCALAPPDATA%\PersonalServer\PersonalServer.exe
```

Re-run this after **any** code change, then fully restart Claude Desktop — the running exe is a
snapshot, not live source.

## Use it from Claude Desktop

1. **Publish** the exe (above).
2. **Edit Claude Desktop's config** at:

   ```
   # Microsoft Store / packaged (MSIX) install — what THIS machine uses.
   # Packaged apps virtualize AppData, so they do NOT read %APPDATA%\Claude\.
   %LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json

   # Standalone (non-Store) install would instead use:
   %APPDATA%\Claude\claude_desktop_config.json
   ```

   Add the `PersonalServer` entry (see [`claude_desktop_config.sample.json`](claude_desktop_config.sample.json)).
   Optionally set `EXPERIENCE_VAULT` in its `env` if your vault isn't at `~/Documents/Design_Exp`:

   ```json
   {
     "mcpServers": {
       "PersonalServer": {
         "command": "C:\\Users\\randl\\AppData\\Local\\PersonalServer\\PersonalServer.exe",
         "env": { "EXPERIENCE_VAULT": "C:\\Users\\randl\\Documents\\Design_Exp" }
       }
     }
   }
   ```

3. **Fully restart Claude Desktop.** Closing the window isn't enough — quit it from the system tray
   (right-click → Quit) and relaunch, so it reloads the config and spawns the server.
4. **Verify:** confirm the tools are listed, then try `find_tendencies` — it reports real counts off
   your vault.

> **Editing the config on the Store build:** quit Claude Desktop *completely* first (tray → Quit,
> confirm no `Claude.exe` remain). The packaged app rewrites `claude_desktop_config.json` on
> preference changes and will clobber edits made while it's running. Troubleshooting logs live next
> to the config: `…\LocalCache\Roaming\Claude\logs\mcp-server-PersonalServer.log`.

## Other ways to run / test

- **Smoke test (no client needed):**
  ```powershell
  powershell -File scripts\smoke-ping.ps1
  ```
  Builds the project, drives the server over stdio (`initialize` → `tools/list` → `tools/call
  ping`), prints the raw JSON-RPC, and asserts `ping`/`pong`.

- **Full test suite:**
  ```powershell
  dotnet test tests\PersonalServer.Tests
  ```

- **MCP Inspector (interactive):**
  ```powershell
  npx @modelcontextprotocol/inspector "$env:LOCALAPPDATA\PersonalServer\PersonalServer.exe"
  ```
