using System.ComponentModel;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using PersonalServer.Data;

namespace PersonalServer.KnowledgeGraph;

/// <summary>The connected subgraph around one experience: linked entities and related experiences.</summary>
[McpServerToolType]
internal static class NeighborTools
{
    [McpServerTool(Name = "neighbors")]
    [Description("Return the neighborhood of one experience: the entities it links to, plus other " +
                 "experiences connected to it by a shared entity or a shared skill (each labeled with " +
                 "the via-name). Ports STARfolio's connection traversal. Pass an experience id (from " +
                 "query or search_nodes).")]
    public static NeighborsResult Neighbors(
        [Description("The id of the experience to find neighbors for.")] string experienceId)
    {
        var (conn, error) = ReadGuard.OpenReadOnly();
        if (error != null) return new NeighborsResult([], [], error);

        using (conn)
        {
            using (var exists = conn!.CreateCommand())
            {
                exists.CommandText = "SELECT 1 FROM v_experiences WHERE id = @id";
                exists.Parameters.AddWithValue("@id", experienceId);
                if (exists.ExecuteScalar() is null)
                    return new NeighborsResult([], [], $"experience not found: {experienceId}");
            }

            var entities = new List<NeighborEntity>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT e.id, e.kind, e.name FROM v_edges g JOIN v_entities e ON e.id = g.dst_id " +
                    "WHERE g.src_kind = 'experience' AND g.src_id = @id AND g.dst_kind = 'entity' ORDER BY e.name";
                cmd.Parameters.AddWithValue("@id", experienceId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    entities.Add(new NeighborEntity(r.GetString(0), r.GetString(1), r.GetString(2)));
            }

            var byExp = new Dictionary<string, (string Title, List<string> ViaEntities, List<string> ViaSkills)>();

            void Add(string expId, string title, string via, bool entity)
            {
                if (!byExp.TryGetValue(expId, out var c))
                    byExp[expId] = c = (title, [], []);
                var list = entity ? c.ViaEntities : c.ViaSkills;
                if (!list.Contains(via)) list.Add(via);
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT x.id, x.title, e.name AS via FROM v_edges g1 " +
                    "JOIN v_edges g2 ON g1.dst_id = g2.dst_id AND g2.src_kind = 'experience' AND g2.dst_kind = 'entity' " +
                    "JOIN v_entities e ON e.id = g1.dst_id " +
                    "JOIN v_experiences x ON x.id = g2.src_id " +
                    "WHERE g1.src_kind = 'experience' AND g1.src_id = @id AND g1.dst_kind = 'entity' AND g2.src_id <> @id";
                cmd.Parameters.AddWithValue("@id", experienceId);
                using var r = cmd.ExecuteReader();
                while (r.Read()) Add(r.GetString(0), r.GetString(1), r.GetString(2), entity: true);
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT x.id, x.title, es1.skill_name AS via FROM v_experience_skills es1 " +
                    "JOIN v_experience_skills es2 ON es1.skill_name = es2.skill_name AND es2.experience_id <> es1.experience_id " +
                    "JOIN v_experiences x ON x.id = es2.experience_id WHERE es1.experience_id = @id";
                cmd.Parameters.AddWithValue("@id", experienceId);
                using var r = cmd.ExecuteReader();
                while (r.Read()) Add(r.GetString(0), r.GetString(1), r.GetString(2), entity: false);
            }

            var connections = byExp
                .Select(kv => new Connection(kv.Key, kv.Value.Title, kv.Value.ViaEntities, kv.Value.ViaSkills))
                .OrderBy(c => c.Title)
                .ToList();

            return new NeighborsResult(entities, connections);
        }
    }
}
