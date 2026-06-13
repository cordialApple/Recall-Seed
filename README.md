# PersonalServer

A minimal [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server written in
C# on **.NET 10**, using the official [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol)
SDK and the `Microsoft.McpServer.ProjectTemplates` template.

It speaks **stdio only** — it's a console / Generic Host app that exchanges JSON-RPC frames over
stdin/stdout. `stdout` is reserved for the protocol; **all logging goes to stderr**.

Right now it exposes a single tool, `ping`, used to verify the server works end-to-end. The
knowledge-graph tooling is intentionally not built yet — see [Next: knowledge graph](#next-knowledge-graph).

## Layout

```
PersonalServer.csproj      # net10.0 console app, MCP packaging metadata
Program.cs                 # Generic Host: stdio transport + WithToolsFromAssembly() + KG TODO
Tools/PingTools.cs         # the `ping` health-check tool ([McpServerToolType])
.mcp/server.json           # MCP server manifest (for NuGet packaging; placeholders)
.vscode/mcp.json           # registers this server with VS Code over stdio
scripts/smoke-ping.ps1     # non-interactive end-to-end smoke test
KnowledgeGraph/README.md   # plan + extension point for the next milestone
```

## The `ping` tool

| | |
|---|---|
| Name | `ping` |
| Arg | `message` *(optional string)* |
| Returns | `pong \| msg=<message> \| utc=<ISO-8601 UTC now>` |

It's a health check: call it to confirm the server is alive and round-tripping requests.

## Run it

```powershell
# Build
dotnet build

# Run the server (a client attaches over stdio)
dotnet run --project PersonalServer.csproj
```

The server reads JSON-RPC from stdin and writes responses to stdout, so running it bare in a
terminal just waits for a client. Attach it from a real client instead (below).

## Test the `ping` tool

### Option A — reusable smoke test (recommended, non-interactive)

```powershell
pwsh ./scripts/smoke-ping.ps1     # or: powershell -File scripts\smoke-ping.ps1
```

It builds the project, drives the server over stdio with `initialize` →
`notifications/initialized` → `tools/list` → `tools/call ping`, prints the raw JSON-RPC
responses, and asserts that `ping` is listed and returns `pong`.

> Note: a real MCP client keeps the stdio pipe open and reads replies as they arrive. Naively
> piping a `.jsonl` file into the server (so stdin hits EOF immediately) can race the server's
> response flush and drop output — the smoke-test script keeps stdin open until the responses
> arrive, which is what a client does.

### Option B — MCP Inspector (interactive)

```powershell
npx @modelcontextprotocol/inspector dotnet run --project PersonalServer.csproj
```

Open the Inspector UI, list tools, and invoke `ping` with `{"message":"hello"}`.

### Option C — VS Code

`.vscode/mcp.json` already registers this server. Open the workspace in VS Code, start the
`PersonalServer` MCP server, and call `ping` from chat.

## Next: knowledge graph

This server is the foundation for a knowledge-graph MCP server. The next milestone adds KG tools
— `query`, `add_entity`, `search_nodes`, `get_schema` — under `KnowledgeGraph/`. The extension
point is already wired:

- `Program.cs` registers tools with `.WithToolsFromAssembly()`, so any class annotated with
  `[McpServerToolType]` is discovered automatically — **no change to `Program.cs` needed** to add
  the KG tools.
- A `TODO(knowledge-graph)` marker in `Program.cs` flags where the registration converges.
- See [`KnowledgeGraph/README.md`](KnowledgeGraph/README.md) for the plan.

These tools are **not implemented yet** — this README and the scaffold deliberately stop at a
working `ping`.
