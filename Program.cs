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

Recall_Seed.Vault.BackendConfig.Apply(Console.Error);
Recall_Seed.Scroll.VerdictReceiver.Register(builder.Services, Console.Error);

await builder.Build().RunAsync();
