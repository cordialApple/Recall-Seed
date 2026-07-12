using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace PersonalServer.Assistant;

/// <summary>Semantic (embedding-based) retrieval, proxied to a running STARfolio.</summary>
[McpServerToolType]
internal static class RetrieveTools
{
    [McpServerTool(Name = "retrieve_semantic")]
    [Description("Semantic search over the experience bank using STARfolio's embeddings (hybrid " +
                 "keyword + vector retrieval). Better than search_nodes for thematic questions like " +
                 "'a time I led under pressure' because it matches meaning, not just keywords. " +
                 "Requires STARfolio to be running; if it is closed this returns " +
                 "error 'starfolio_not_running' and you should fall back to search_nodes or query.")]
    public static async Task<RetrieveResult> RetrieveSemantic(
        [Description("The thematic query to search for.")] string query,
        [Description("Maximum number of experiences to return (1-100, default 10).")] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new RetrieveResult([], "query must not be empty");

        var (body, error) = await Loopback.PostAsync("/retrieve", new { query, limit = Math.Clamp(limit, 1, 100) });
        if (error != null)
            return new RetrieveResult([], error);

        var hits = new List<SemanticHit>();
        if (body!.Value.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var el in arr.EnumerateArray())
                hits.Add(new SemanticHit(
                    Loopback.Str(el, "experience_id") ?? "",
                    Loopback.Str(el, "title") ?? "",
                    Loopback.Num(el, "score"),
                    Loopback.Str(el, "snippet")));

        return new RetrieveResult(hits);
    }
}
