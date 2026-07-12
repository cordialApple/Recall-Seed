using System.ComponentModel;
using System.Text;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using PersonalServer.Data;

namespace PersonalServer.KnowledgeGraph;

/// <summary>Structured retrieval over the experience bank using named filters (no raw SQL).</summary>
[McpServerToolType]
internal static class QueryTools
{
    [McpServerTool(Name = "query")]
    [Description("Structured retrieval over the experience bank using named filters (never raw SQL). " +
                 "Filter by any combination of skills, tags, context, status, and a happened-date range; " +
                 "returns experience cards with their skills, tags, and metrics. Use search_nodes for " +
                 "free-text search and neighbors for graph traversal.")]
    public static QueryResult Query(
        [Description("Only experiences having at least one of these skill names.")] string[]? skills = null,
        [Description("Only experiences having at least one of these tag names.")] string[]? tags = null,
        [Description("Filter by context: work, project, class, or other.")] string? context = null,
        [Description("Filter by status: draft or confirmed.")] string? status = null,
        [Description("Inclusive lower bound on happened_start (ISO date, e.g. 2025-01-01).")] string? dateFrom = null,
        [Description("Inclusive upper bound on happened_start (ISO date).")] string? dateTo = null,
        [Description("Maximum number of experiences to return (1-200, default 25).")] int limit = 25)
    {
        var (conn, error) = ReadGuard.OpenReadOnly();
        if (error != null) return new QueryResult([], error);

        using (conn)
        {
            var where = new List<string>();
            using var cmd = conn!.CreateCommand();

            if (skills is { Length: > 0 })
                where.Add($"id IN (SELECT experience_id FROM v_experience_skills WHERE skill_name IN ({InParams(cmd, "s", skills)}))");
            if (tags is { Length: > 0 })
                where.Add($"id IN (SELECT experience_id FROM v_experience_tags WHERE tag_name IN ({InParams(cmd, "t", tags)}))");
            if (!string.IsNullOrWhiteSpace(context)) { where.Add("context = @context"); cmd.Parameters.AddWithValue("@context", context); }
            if (!string.IsNullOrWhiteSpace(status)) { where.Add("status = @status"); cmd.Parameters.AddWithValue("@status", status); }
            if (!string.IsNullOrWhiteSpace(dateFrom)) { where.Add("happened_start >= @dateFrom"); cmd.Parameters.AddWithValue("@dateFrom", dateFrom); }
            if (!string.IsNullOrWhiteSpace(dateTo)) { where.Add("happened_start <= @dateTo"); cmd.Parameters.AddWithValue("@dateTo", dateTo); }

            var sql = new StringBuilder(
                "SELECT id, title, context, status, happened_start, happened_end FROM v_experiences");
            if (where.Count > 0) sql.Append(" WHERE ").Append(string.Join(" AND ", where));
            sql.Append(" ORDER BY COALESCE(happened_start, created_at) DESC, title LIMIT @limit");
            cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 200));
            cmd.CommandText = sql.ToString();

            var rows = new List<(string Id, string Title, string Context, string Status, string? Start, string? End)>();
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                             r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));

            var ids = rows.Select(x => x.Id).ToList();
            var skillMap = GroupStrings(conn, "SELECT experience_id, skill_name FROM v_experience_skills", ids);
            var tagMap = GroupStrings(conn, "SELECT experience_id, tag_name FROM v_experience_tags", ids);
            var metricMap = GroupMetrics(conn, ids);

            var cards = rows.Select(x => new ExperienceCard(
                x.Id, x.Title, x.Context, x.Status, x.Start, x.End,
                skillMap.GetValueOrDefault(x.Id, []),
                tagMap.GetValueOrDefault(x.Id, []),
                metricMap.GetValueOrDefault(x.Id, []))).ToList();

            return new QueryResult(cards);
        }
    }

    static string InParams(SqliteCommand cmd, string prefix, string[] values)
    {
        var names = new List<string>(values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            var name = $"@{prefix}{i}";
            names.Add(name);
            cmd.Parameters.AddWithValue(name, values[i]);
        }
        return string.Join(",", names);
    }

    static Dictionary<string, List<string>> GroupStrings(SqliteConnection conn, string baseSql, List<string> ids)
    {
        var map = new Dictionary<string, List<string>>();
        if (ids.Count == 0) return map;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"{baseSql} WHERE experience_id IN ({IdParams(cmd, ids)})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            (map.TryGetValue(id, out var list) ? list : map[id] = []).Add(r.GetString(1));
        }
        return map;
    }

    static Dictionary<string, List<MetricDto>> GroupMetrics(SqliteConnection conn, List<string> ids)
    {
        var map = new Dictionary<string, List<MetricDto>>();
        if (ids.Count == 0) return map;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT experience_id, label, value, unit FROM v_experience_metrics WHERE experience_id IN ({IdParams(cmd, ids)})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            var metric = new MetricDto(r.GetString(1), r.IsDBNull(2) ? null : r.GetDouble(2), r.IsDBNull(3) ? null : r.GetString(3));
            (map.TryGetValue(id, out var list) ? list : map[id] = []).Add(metric);
        }
        return map;
    }

    static string IdParams(SqliteCommand cmd, List<string> ids)
    {
        var names = new List<string>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            var name = $"@id{i}";
            names.Add(name);
            cmd.Parameters.AddWithValue(name, ids[i]);
        }
        return string.Join(",", names);
    }
}
