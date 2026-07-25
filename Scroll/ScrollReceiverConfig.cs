namespace Recall_Seed.Scroll;

/// <summary>
/// Opt-in config for the loopback verdict receiver. The receiver stays OFF unless SCROLL_VERDICT_PORT
/// names a valid port — the default remains a pure stdio MCP server with no listening socket. Resolve
/// is pure (env dictionary in, port-or-null out) so it is unit-testable without touching the environment.
/// </summary>
internal static class ScrollReceiverConfig
{
    public const string PortEnv = "SCROLL_VERDICT_PORT";

    public static int? ResolvePort(IReadOnlyDictionary<string, string?> env)
    {
        if (!env.TryGetValue(PortEnv, out var raw) || string.IsNullOrWhiteSpace(raw)) return null;
        if (!int.TryParse(raw.Trim(), out var port)) return null;
        if (port is < 1 or > 65535) return null;
        return port;
    }
}
