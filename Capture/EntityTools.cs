using System.ComponentModel;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using PersonalServer.Data;

namespace PersonalServer.Capture;

/// <summary>Adds graph entities and edges back into the STARfolio bank, idempotently.</summary>
[McpServerToolType]
internal static class EntityTools
{
    static readonly string[] EntityKinds = ["person", "team", "project", "org", "tool", "other"];
    static readonly string[] NodeKinds = ["experience", "entity"];

    [McpServerTool(Name = "add_entity")]
    [Description("Add (or find) a knowledge-graph entity by kind and name, returning its stable id. " +
                 "Idempotent: calling it again with the same kind and name returns the existing id " +
                 "rather than creating a duplicate. Kinds: person, team, project, org, tool, other.")]
    public static AddEntityResult AddEntity(
        [Description("Entity kind: one of person, team, project, org, tool, other.")] string kind,
        [Description("Entity display name, e.g. 'Payments Platform'.")] string name)
    {
        if (!EntityKinds.Contains(kind))
            return new AddEntityResult(null, $"invalid kind '{kind}'; must be one of: {string.Join(", ", EntityKinds)}");
        if (string.IsNullOrWhiteSpace(name))
            return new AddEntityResult(null, "name must not be empty");

        var (conn, error) = WriteGuard.OpenReadWrite();
        if (error != null) return new AddEntityResult(null, error);

        using (conn)
        {
            try
            {
                using (var insert = conn!.CreateCommand())
                {
                    insert.CommandText = "INSERT OR IGNORE INTO entities (id, kind, name) VALUES (@id, @kind, @name)";
                    insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                    insert.Parameters.AddWithValue("@kind", kind);
                    insert.Parameters.AddWithValue("@name", name);
                    insert.ExecuteNonQuery();
                }

                using var sel = conn.CreateCommand();
                sel.CommandText = "SELECT id FROM entities WHERE kind = @kind AND name = @name";
                sel.Parameters.AddWithValue("@kind", kind);
                sel.Parameters.AddWithValue("@name", name);
                return new AddEntityResult(sel.ExecuteScalar() as string);
            }
            catch (SqliteException ex)
            {
                return new AddEntityResult(null, ex.Message);
            }
        }
    }

    [McpServerTool(Name = "add_edge")]
    [Description("Add a directed relationship between two graph nodes (experiences or entities), " +
                 "e.g. an experience 'mentions' a project entity. Idempotent on " +
                 "(src_kind, src_id, rel, dst_kind, dst_id). Both endpoints must already exist. " +
                 "Node kinds are 'experience' or 'entity'; get ids from query, search_nodes, or add_entity.")]
    public static AddEdgeResult AddEdge(
        [Description("Source node kind: 'experience' or 'entity'.")] string srcKind,
        [Description("Source node id.")] string srcId,
        [Description("Relationship name, e.g. 'mentions'.")] string rel,
        [Description("Destination node kind: 'experience' or 'entity'.")] string dstKind,
        [Description("Destination node id.")] string dstId)
    {
        if (!NodeKinds.Contains(srcKind) || !NodeKinds.Contains(dstKind))
            return new AddEdgeResult(null, "src_kind and dst_kind must each be 'experience' or 'entity'");
        if (string.IsNullOrWhiteSpace(rel))
            return new AddEdgeResult(null, "rel must not be empty");

        var (conn, error) = WriteGuard.OpenReadWrite();
        if (error != null) return new AddEdgeResult(null, error);

        using (conn)
        {
            try
            {
                if (!NodeExists(conn!, srcKind, srcId))
                    return new AddEdgeResult(null, $"source node not found: {srcKind}/{srcId}");
                if (!NodeExists(conn!, dstKind, dstId))
                    return new AddEdgeResult(null, $"destination node not found: {dstKind}/{dstId}");

                using (var insert = conn!.CreateCommand())
                {
                    insert.CommandText =
                        "INSERT OR IGNORE INTO edges (id, src_kind, src_id, rel, dst_kind, dst_id) " +
                        "VALUES (@id, @sk, @si, @rel, @dk, @di)";
                    insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                    insert.Parameters.AddWithValue("@sk", srcKind);
                    insert.Parameters.AddWithValue("@si", srcId);
                    insert.Parameters.AddWithValue("@rel", rel);
                    insert.Parameters.AddWithValue("@dk", dstKind);
                    insert.Parameters.AddWithValue("@di", dstId);
                    insert.ExecuteNonQuery();
                }

                using var sel = conn.CreateCommand();
                sel.CommandText =
                    "SELECT id FROM edges WHERE src_kind = @sk AND src_id = @si AND rel = @rel " +
                    "AND dst_kind = @dk AND dst_id = @di";
                sel.Parameters.AddWithValue("@sk", srcKind);
                sel.Parameters.AddWithValue("@si", srcId);
                sel.Parameters.AddWithValue("@rel", rel);
                sel.Parameters.AddWithValue("@dk", dstKind);
                sel.Parameters.AddWithValue("@di", dstId);
                return new AddEdgeResult(sel.ExecuteScalar() as string);
            }
            catch (SqliteException ex)
            {
                return new AddEdgeResult(null, ex.Message);
            }
        }
    }

    // Edges are polymorphic (an endpoint is an experience OR an entity), so a single SQL foreign
    // key can't express the constraint — validate the referenced node in code against the right view.
    static bool NodeExists(SqliteConnection conn, string kind, string id)
    {
        var view = kind == "experience" ? "v_experiences" : "v_entities";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM {view} WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteScalar() is not null;
    }
}
