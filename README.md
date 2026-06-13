# PersonalServer

A minimal [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server written in
C# on **.NET 10**, using the official [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol)
SDK. It is built to be used with the **Claude Desktop** client.

It speaks **stdio only** — a console / Generic Host app that exchanges JSON-RPC over
stdin/stdout. `stdout` is reserved for the protocol; **all logging goes to stderr**.

Right now it exposes a single tool, `ping`, to verify the server works end-to-end. The
knowledge-graph tooling is intentionally not built yet — see [Next: knowledge graph](#next-knowledge-graph).

> **How an MCP stdio server runs:** it is *not* a daemon and you never start it yourself. The
> client (Claude Desktop) launches the server as a child process on demand and talks to it over
> stdio. So "always available" just means the client can spawn it instantly — which is why we
> publish a fast, self-contained `.exe` and point Claude Desktop at it (no `dotnet run`, no SDK
> needed at launch). A Windows service is *not* used: a detached service has no stdio pipe for a
> client to attach to.

## Layout

```
PersonalServer.csproj             # net10.0 console app, self-contained single-file publish
Program.cs                        # Generic Host: stdio transport + WithToolsFromAssembly() + KG TODO
Tools/PingTools.cs                # the `ping` health-check tool ([McpServerToolType])
claude_desktop_config.sample.json # reference snippet for Claude Desktop's config
.vscode/mcp.json                  # optional: same server for VS Code / MCP Inspector dev
.mcp/server.json                  # MCP manifest (NuGet packaging; placeholders, unused for stdio)
scripts/smoke-ping.ps1            # non-interactive end-to-end smoke test
KnowledgeGraph/README.md          # plan + extension point for the next milestone
```

## The `ping` tool

| | |
|---|---|
| Name | `ping` |
| Arg | `message` *(optional string)* |
| Returns | `pong \| msg=<message> \| utc=<ISO-8601 UTC now>` |

## Build & publish

The server is published as a **self-contained, single-file exe** (no .NET runtime required on the
target) to a stable location outside the repo, so the artifact survives `dotnet clean` and repo
moves:

```powershell
dotnet publish -c Release -r win-x64 -o "$env:LOCALAPPDATA\PersonalServer"
# -> %LOCALAPPDATA%\PersonalServer\PersonalServer.exe
```

Re-run this after any code change, then restart Claude Desktop (below).

## Use it from Claude Desktop

1. **Publish** the exe (above).
2. **Edit Claude Desktop's config** at:

   ```
   %APPDATA%\Claude\claude_desktop_config.json
   ```

   Add the `PersonalServer` entry (see [`claude_desktop_config.sample.json`](claude_desktop_config.sample.json)).
   If the file already has other servers, merge this into the existing `mcpServers` object:

   ```json
   {
     "mcpServers": {
       "PersonalServer": {
         "command": "C:\\Users\\randl\\AppData\\Local\\PersonalServer\\PersonalServer.exe"
       }
     }
   }
   ```

3. **Fully restart Claude Desktop.** Closing the window isn't enough — quit it from the system
   tray (right-click → Quit) and relaunch, so it reloads the config and spawns the server.
4. **Verify:** open the tools/MCP menu in Claude Desktop and confirm `PersonalServer` → `ping` is
   listed, then ask Claude to *"call the ping tool with message hello."* You should get back
   `pong | msg=hello | utc=...`.

> Note: editing the server's source requires a **re-publish** (step 1) and a **restart** of Claude
> Desktop to take effect — the running exe is a snapshot, not live source.

## Other ways to run / test

- **Reusable smoke test (no client needed):**
  ```powershell
  powershell -File scripts\smoke-ping.ps1
  ```
  Builds the project, drives the server over stdio (`initialize` → `notifications/initialized` →
  `tools/list` → `tools/call ping`), prints the raw JSON-RPC, and asserts `ping`/`pong`.

- **MCP Inspector (interactive):**
  ```powershell
  npx @modelcontextprotocol/inspector "$env:LOCALAPPDATA\PersonalServer\PersonalServer.exe"
  ```

- **VS Code:** `.vscode/mcp.json` points at the same published exe.

## Next: knowledge graph

This server is the foundation for a knowledge-graph MCP server. The next milestone adds KG tools
— `query`, `add_entity`, `search_nodes`, `get_schema` — under `KnowledgeGraph/`. The extension
point is already wired:

- `Program.cs` registers tools with `.WithToolsFromAssembly()`, so any class annotated with
  `[McpServerToolType]` is discovered automatically — **no change to `Program.cs` needed** to add
  the KG tools.
- A `TODO(knowledge-graph)` marker in `Program.cs` flags where the registration converges.
- See [`KnowledgeGraph/README.md`](KnowledgeGraph/README.md) for the plan.

These tools are **not implemented yet** — the scaffold deliberately stops at a working `ping`.
