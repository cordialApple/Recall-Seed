using System.Text.RegularExpressions;

namespace Recall_Seed.Scroll;

/// <summary>
/// Resolves where Scroll is hosted (SCROLL_BASE_URL) and composes the two URLs the seed tool emits: the
/// programmatic spawn URL and the loopback contract-6 callback. Pure (env dictionary in, value out) so it
/// is unit-testable without touching the environment; the process-env convenience wrappers sit on top.
/// </summary>
internal static partial class ScrollSpawnConfig
{
    public const string BaseUrlEnv = "SCROLL_BASE_URL";
    public const string DefaultBaseUrl = "http://localhost:5173";

    [GeneratedRegex(@"^https?://")]
    private static partial Regex HttpUrl();

    public static bool IsHttpUrl(string? value) => value is not null && HttpUrl().IsMatch(value);

    /// <summary>Default when unset; null only when SCROLL_BASE_URL is set to something invalid.</summary>
    public static string? ResolveBaseUrl(IReadOnlyDictionary<string, string?> env)
    {
        var raw = env.TryGetValue(BaseUrlEnv, out var v) ? v : null;
        if (string.IsNullOrWhiteSpace(raw)) return DefaultBaseUrl;
        raw = raw.Trim();
        if (!IsHttpUrl(raw) || raw.Contains('#')) return null;
        return raw.TrimEnd('/');
    }

    public static string? ResolveBaseUrl()
        => ResolveBaseUrl(new Dictionary<string, string?> { [BaseUrlEnv] = Environment.GetEnvironmentVariable(BaseUrlEnv) });

    public static string SpawnUrl(string baseUrl, string param) => $"{baseUrl}#/es/new?s={param}";

    /// <summary>The consumer's own loopback receiver, with the correlation id baked in as opaque query data.</summary>
    public static string CallbackUrl(int port, string correlationId)
        => $"http://127.0.0.1:{port}/?c={Uri.EscapeDataString(correlationId)}";

    /// <summary>Receiver port from SCROLL_VERDICT_PORT, or null when the loopback receiver is disabled.</summary>
    public static int? ReceiverPort()
        => ScrollReceiverConfig.ResolvePort(new Dictionary<string, string?>
        {
            [ScrollReceiverConfig.PortEnv] = Environment.GetEnvironmentVariable(ScrollReceiverConfig.PortEnv),
        });
}
