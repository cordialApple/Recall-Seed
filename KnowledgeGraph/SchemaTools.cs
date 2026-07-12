using System.ComponentModel;
using ModelContextProtocol.Server;
using PersonalServer.Data;

namespace PersonalServer.KnowledgeGraph;

/// <summary>
/// Reports the knowledge-graph vocabulary and confirms the STARfolio contract views are present.
/// </summary>
[McpServerToolType]
internal static class SchemaTools
{
    internal static readonly string[] ContractViews =
    [
        "v_experiences", "v_experience_skills", "v_experience_tags",
        "v_experience_metrics", "v_entities", "v_edges", "v_experience_sources"
    ];

    [McpServerTool(Name = "get_schema")]
    [Description("Return the knowledge-graph schema exposed over MCP: entity kinds, stored and " +
                 "derived edge relations, experience fields, the read/write tool names, and which " +
                 "contract views are present in the STARfolio database. Also serves as a " +
                 "connectivity check — call it first to confirm the database is reachable.")]
    public static SchemaResult GetSchema()
    {
        var (conn, error) = ReadGuard.OpenReadOnly();
        if (error != null)
            return new SchemaResult([], [], [], [], [], [], [], [], error);

        using (conn)
        {
            var views = new HashSet<string>();
            var relations = new List<string>();

            using (var cmd = conn!.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'view'";
                using var r = cmd.ExecuteReader();
                while (r.Read()) views.Add(r.GetString(0));
            }

            if (views.Contains("v_edges"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT rel FROM v_edges ORDER BY rel";
                using var r = cmd.ExecuteReader();
                while (r.Read()) relations.Add(r.GetString(0));
            }

            var missing = ContractViews.Where(v => !views.Contains(v)).ToArray();

            return new SchemaResult(
                EntityKinds: ["person", "team", "project", "org", "tool", "other"],
                EdgeRelations: relations,
                DerivedRelations: ["shared_entity", "shared_skill"],
                ExperienceFields:
                [
                    "id", "title", "situation", "task", "action", "result_text",
                    "context", "status", "happened_start", "happened_end"
                ],
                ReadTools: ["get_schema", "query", "search_nodes", "neighbors"],
                WriteTools: ["add_entity", "add_edge", "capture_experience"],
                ContractViews: ContractViews,
                MissingViews: missing);
        }
    }
}
