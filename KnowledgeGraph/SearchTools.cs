using System.ComponentModel;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using PersonalServer.Data;

namespace PersonalServer.KnowledgeGraph;

/// <summary>Free-text / fuzzy search over experiences (FTS5) and entity names.</summary>
[McpServerToolType]
internal static class SearchTools
{
    static readonly string[] EntityKinds = ["person", "team", "project", "org", "tool", "other"];

    [McpServerTool(Name = "search_nodes")]
    [Description("Free-text search over the knowledge graph: experiences via full-text (FTS5) and " +
                 "entities by name. Returns ranked nodes with a snippet. Optionally restrict to node " +
                 "types with 'kinds' (any of: experience, person, team, project, org, tool, other). " +
                 "For thematic queries that need meaning rather than keywords, prefer retrieve_semantic " +
                 "when STARfolio is running.")]
    public static SearchResult SearchNodes(
        [Description("The text to search for.")] string query,
        [Description("Restrict to these node types (experience and/or entity kinds). Empty = all.")] string[]? kinds = null,
        [Description("Maximum number of nodes to return (1-100, default 20).")] int limit = 20)
    {
        var (conn, error) = ReadGuard.OpenReadOnly();
        if (error != null) return new SearchResult([], error);

        using (conn)
        {
            var cap = Math.Clamp(limit, 1, 100);
            var wantExperiences = kinds is null || kinds.Length == 0 || kinds.Contains("experience");
            var wantEntityKinds = (kinds is null || kinds.Length == 0)
                ? EntityKinds
                : kinds.Where(EntityKinds.Contains).ToArray();

            var hits = new List<NodeHit>();

            var match = FtsQuery.ToMatch(query);
            if (wantExperiences && match != null)
            {
                using var cmd = conn!.CreateCommand();
                cmd.CommandText =
                    "SELECT e.id, e.title, snippet(experiences_fts, -1, '[', ']', '…', 10) AS snip " +
                    "FROM experiences_fts JOIN experiences e ON e.rowid = experiences_fts.rowid " +
                    "WHERE experiences_fts MATCH @m ORDER BY rank LIMIT @lim";
                cmd.Parameters.AddWithValue("@m", match);
                cmd.Parameters.AddWithValue("@lim", cap);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    hits.Add(new NodeHit("experience", r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
            }

            if (wantEntityKinds.Length > 0)
            {
                using var cmd = conn!.CreateCommand();
                var kindParams = new List<string>();
                for (var i = 0; i < wantEntityKinds.Length; i++)
                {
                    var name = $"@k{i}";
                    kindParams.Add(name);
                    cmd.Parameters.AddWithValue(name, wantEntityKinds[i]);
                }
                cmd.CommandText =
                    $"SELECT id, kind, name FROM v_entities " +
                    $"WHERE kind IN ({string.Join(",", kindParams)}) " +
                    "AND lower(name) LIKE '%' || lower(@q) || '%' ORDER BY name LIMIT @lim";
                cmd.Parameters.AddWithValue("@q", query);
                cmd.Parameters.AddWithValue("@lim", cap);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    hits.Add(new NodeHit(r.GetString(1), r.GetString(0), r.GetString(2), null));
            }

            return new SearchResult(hits.Take(cap).ToList());
        }
    }
}
