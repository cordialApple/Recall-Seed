using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;

/// <summary>
/// Health-check tools for verifying the MCP server is alive and responding end-to-end over stdio.
/// </summary>
[McpServerToolType]
internal static class PingTools
{
    [McpServerTool(Name = "ping")]
    [Description("Health check that verifies the MCP server is alive and responding. " +
                 "Echoes an optional message back and stamps the current UTC time, " +
                 "useful for confirming connectivity end-to-end.")]
    public static string Ping(
        [Description("Optional message to echo back in the response.")] string? message = null)
    {
        var utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        return $"pong | msg={message ?? string.Empty} | utc={utc}";
    }
}
