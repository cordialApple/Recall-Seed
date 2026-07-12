namespace PersonalServer.Data;

/// <summary>
/// Builds an FTS5 MATCH expression the STARfolio way: each whitespace token quoted, the last
/// token turned into a prefix query, joined by implicit AND. See SCHEMA-CONTRACT.md.
/// </summary>
public static class FtsQuery
{
    public static string? ToMatch(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var tokens = input
            .Replace('"', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return null;

        var parts = new List<string>(tokens.Length);
        for (var i = 0; i < tokens.Length; i++)
        {
            var quoted = $"\"{tokens[i]}\"";
            parts.Add(i == tokens.Length - 1 ? quoted + "*" : quoted);
        }
        return string.Join(' ', parts);
    }
}
