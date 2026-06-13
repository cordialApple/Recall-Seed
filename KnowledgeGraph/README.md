# KnowledgeGraph (extension point — not implemented yet)

This folder marks where the knowledge-graph (KG) capability will live. **Nothing here is
implemented yet** — the current server intentionally ships only the `ping` health-check tool.

## Planned tools

The next milestone will add these MCP tools, each as a method on a class annotated with
`[McpServerToolType]` so it is auto-discovered by `.WithToolsFromAssembly()` in `Program.cs`:

| Tool | Purpose (planned) |
|---|---|
| `query` | Run a query against the knowledge graph and return matching nodes/edges. |
| `add_entity` | Add (or upsert) an entity/node, with its type and properties. |
| `search_nodes` | Full-text / fuzzy search over node labels and properties. |
| `get_schema` | Return the graph schema: known entity types, relationship types, properties. |

## How to add them (when the time comes)

1. Create one or more classes under `KnowledgeGraph/`, e.g. `KnowledgeGraph/KnowledgeGraphTools.cs`.
2. Annotate each class with `[McpServerToolType]` and each tool method with
   `[McpServerTool(Name = "...")]` + `[Description("...")]` — mirroring `Tools/PingTools.cs`.
3. That's it for registration: `Program.cs` already calls `.WithToolsFromAssembly()`, which
   discovers `[McpServerToolType]` classes in this assembly. No edits to `Program.cs` are
   required (see the `TODO(knowledge-graph)` marker there).
4. Add the backing store / graph engine and wire it in via dependency injection
   (`builder.Services.AddSingleton<...>()`), then inject it into the tool class constructor.

## Out of scope for now

- Persistence / storage engine choice.
- Graph schema definition.
- Any of the four tools above.

Keep this milestone focused: a clean, working stdio server with `ping`. The KG work builds on top.
