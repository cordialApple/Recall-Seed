using System.Text.Json;

namespace Recall_Seed.Scroll;

/// <summary>
/// contract-6 verdict payload Scroll POSTs fire-and-forget to its resultCallbackUrl on submit.
/// Consumer-blind: pass/fail/counts only, no per-test detail. Wire schema is the Scroll repo's
/// docs/contracts/result-payload.v1.json. TryParse is strict on every required field (type, enum,
/// range) but tolerates unknown extra fields so a future minor wire rev still delivers.
/// </summary>
internal sealed record ScrollVerdict(
    string EndpointId, string Status, bool WithinBudget, int Passed, int Total, long At)
{
    static readonly HashSet<string> ValidStatus = new(StringComparer.Ordinal) { "pass", "fail", "tle", "error" };

    public static bool TryParse(ReadOnlyMemory<byte> utf8Json, out ScrollVerdict? verdict, out string? error)
    {
        verdict = null;
        error = null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(utf8Json); }
        catch (JsonException ex) { error = "malformed JSON: " + ex.Message; return false; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "payload is not a JSON object"; return false; }

            if (!ReqString(root, "endpointId", out var endpointId, out error)) return false;
            if (endpointId!.Length < 1) { error = "endpointId must be non-empty"; return false; }

            if (!ReqString(root, "status", out var status, out error)) return false;
            if (!ValidStatus.Contains(status!)) { error = $"status '{status}' is not one of pass|fail|tle|error"; return false; }

            if (!ReqBool(root, "withinBudget", out var withinBudget, out error)) return false;
            if (!ReqInt(root, "passed", 0, out var passed, out error)) return false;
            if (!ReqInt(root, "total", 1, out var total, out error)) return false;
            if (!ReqEpochMs(root, "at", out var at, out error)) return false;

            verdict = new ScrollVerdict(endpointId!, status!, withinBudget, passed, total, at);
            return true;
        }
    }

    static bool RequireField(JsonElement root, string name, out JsonElement el, out string? error)
    {
        error = null;
        if (root.TryGetProperty(name, out el)) return true;
        error = $"missing required field '{name}'";
        return false;
    }

    static bool ReqString(JsonElement root, string name, out string? value, out string? error)
    {
        value = null;
        if (!RequireField(root, name, out var el, out error)) return false;
        if (el.ValueKind != JsonValueKind.String) { error = $"'{name}' must be a string"; return false; }
        value = el.GetString();
        return true;
    }

    static bool ReqBool(JsonElement root, string name, out bool value, out string? error)
    {
        value = false;
        if (!RequireField(root, name, out var el, out error)) return false;
        if (el.ValueKind != JsonValueKind.True && el.ValueKind != JsonValueKind.False) { error = $"'{name}' must be a boolean"; return false; }
        value = el.GetBoolean();
        return true;
    }

    static bool ReqInt(JsonElement root, string name, int min, out int value, out string? error)
    {
        value = 0;
        if (!RequireField(root, name, out var el, out error)) return false;
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out value)) { error = $"'{name}' must be an integer"; return false; }
        if (value < min) { error = $"'{name}' must be >= {min}"; return false; }
        return true;
    }

    static bool ReqEpochMs(JsonElement root, string name, out long value, out string? error)
    {
        value = 0;
        if (!RequireField(root, name, out var el, out error)) return false;
        if (el.ValueKind != JsonValueKind.Number) { error = $"'{name}' must be a number"; return false; }
        // Contract types 'at' as a JSON number (epoch-ms); accept an integer directly, tolerate a
        // decimal point by truncating (epoch-ms is integral, and 1.7e12 is exact in a double).
        if (el.TryGetInt64(out value)) return true;
        value = (long)el.GetDouble();
        return true;
    }
}
