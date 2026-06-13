using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Configure all logs to go to stderr (stdout is used for the MCP protocol messages).
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Add the MCP services: the stdio transport and all tools discovered in this assembly.
// Tool classes are auto-discovered via the [McpServerToolType] attribute, so adding a new
// tool is just a matter of creating an annotated class — no edits are needed here.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// TODO(knowledge-graph): extension point for the next milestone.
// The planned KG tools — query, add_entity, search_nodes, get_schema — will live under
// KnowledgeGraph/ as classes annotated with [McpServerToolType], so WithToolsFromAssembly()
// above will pick them up automatically with no change to this file. See KnowledgeGraph/README.md.

await builder.Build().RunAsync();
