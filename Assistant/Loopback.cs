using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PersonalServer.Assistant;

internal sealed record LoopbackInfo(int Port, string Token);

/// <summary>
/// Discovers and calls STARfolio's localhost-only loopback API. STARfolio writes a
/// {port, token} file under its userData while running; the C# side reads it, never guesses a
/// port, and treats a missing file or any connection failure as "STARfolio not running" so the
/// AI-proxy tools degrade cleanly. The token authenticates to 127.0.0.1 only and is never logged.
/// See SCHEMA-CONTRACT.md "AI bridge".
/// </summary>
internal static class Loopback
{
    public const string FileEnvVar = "SUPERSTAR_LOOPBACK_FILE";
    public const string NotRunning = "starfolio_not_running";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static string ResolveFilePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(FileEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "STARfolio", "loopback.json");
    }

    static LoopbackInfo? ReadInfo()
    {
        try
        {
            var path = ResolveFilePath();
            if (!File.Exists(path))
                return null;
            var info = JsonSerializer.Deserialize<LoopbackInfo>(File.ReadAllText(path), Json);
            return info is { Port: > 0 } ? info : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// POST <paramref name="payload"/> as JSON to a loopback endpoint. Returns the parsed response
    /// body, or an error: <see cref="NotRunning"/> when the app is closed/unreachable, or a
    /// descriptive message for an HTTP error / malformed response.
    /// </summary>
    public static async Task<(JsonElement? Body, string? Error)> PostAsync(string path, object payload)
    {
        var info = ReadInfo();
        if (info is null)
            return (null, NotRunning);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{info.Port}{path}")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {info.Token}");

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return (null, $"starfolio loopback error: HTTP {(int)resp.StatusCode}");

            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            return (doc.RootElement.Clone(), null);
        }
        catch (HttpRequestException)
        {
            return (null, NotRunning);
        }
        catch (TaskCanceledException)
        {
            return (null, NotRunning);
        }
        catch (JsonException)
        {
            return (null, "starfolio loopback returned malformed JSON");
        }
    }

    public static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static double Num(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
