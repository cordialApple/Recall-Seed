using System.Globalization;
using Microsoft.Data.Sqlite;
using PersonalServer.Data;

namespace PersonalServer.Vault;

/// <summary>
/// Reads and writes STARfolio's SQLite bank (superstar.db) behind the IExperienceStore seam, via
/// the stable v_* contract views (read) and the writable base tables (write). The SQL contract has
/// no per-beat confidence or gap-question concept — those are vault-native. So on read, confidence
/// is derived from status (confirmed → high, draft → medium) and gaps are empty; on write, gaps,
/// confidence, and entities are dropped rather than mis-encoded into STARfolio's typed graph.
/// </summary>
internal sealed class SqliteStore(string? dbPath = null) : IExperienceStore
{
    public (IReadOnlyList<Experience> Items, string? Error) LoadAll()
    {
        var (conn, error) = DbGuard.OpenReadOnly(dbPath);
        if (error != null) return ([], error);

        using (conn)
        try
        {
            var skills = GroupSkills(conn!);
            var tags = GroupStrings(conn!, "SELECT experience_id, tag_name FROM v_experience_tags");
            var metrics = GroupMetrics(conn!);
            var entities = GroupEntities(conn!);

            var items = new List<Experience>();
            using var cmd = conn!.CreateCommand();
            cmd.CommandText =
                "SELECT id, title, situation, task, action, result_text, context, status, updated_at FROM v_experiences";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                var status = r.GetString(7);
                items.Add(new Experience(
                    Id: id,
                    Title: r.GetString(1),
                    Context: r.GetString(6),
                    Status: status,
                    Confidence: ConfidenceFor(status, r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)),
                    Skills: skills.GetValueOrDefault(id, []),
                    Tags: tags.GetValueOrDefault(id, []),
                    Metrics: metrics.GetValueOrDefault(id, []),
                    Entities: entities.GetValueOrDefault(id, []),
                    Situation: r.GetString(2),
                    Task: r.GetString(3),
                    Action: r.GetString(4),
                    Result: r.GetString(5),
                    Gaps: [],
                    Path: "",
                    UpdatedUtc: ParseUtc(r.IsDBNull(8) ? null : r.GetString(8))));
            }
            return (items, null);
        }
        catch (SqliteException ex) { return ([], ex.Message); }
    }

    public IReadOnlyList<string> KnownEntities()
    {
        var (conn, error) = DbGuard.OpenReadOnly(dbPath);
        if (error != null) return [];
        using (conn)
        try
        {
            var names = new List<string>();
            using var cmd = conn!.CreateCommand();
            cmd.CommandText = "SELECT name FROM v_entities ORDER BY name COLLATE NOCASE";
            using var r = cmd.ExecuteReader();
            while (r.Read()) names.Add(r.GetString(0));
            return names;
        }
        catch (SqliteException) { return []; }
    }

    public (string Id, string Path, string? Error) Write(
        string id, string title, string context, string status,
        (string Beat, string Text, string Confidence)[] beats,
        IReadOnlyList<VaultSkill> skills, IReadOnlyList<string> tags,
        IReadOnlyList<VaultMetric> metrics, IReadOnlyList<string> entities,
        IReadOnlyList<string> gaps)
    {
        var (conn, error) = DbGuard.OpenReadWrite(dbPath);
        if (error != null) return ("", "", error);

        using (conn)
        {
            try
            {
                using var tx = conn!.BeginTransaction();
                string Beat(string name) => beats.FirstOrDefault(b => b.Beat.Equals(name, StringComparison.OrdinalIgnoreCase)).Text ?? "";

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "INSERT INTO experiences (id, title, situation, task, action, result_text, context, status, created_at, updated_at) " +
                        "VALUES (@id, @title, @situation, @task, @action, @result, @context, @status, datetime('now'), datetime('now')) " +
                        "ON CONFLICT(id) DO UPDATE SET title=@title, situation=@situation, task=@task, action=@action, " +
                        "result_text=@result, context=@context, status=@status, updated_at=datetime('now')";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@title", title);
                    cmd.Parameters.AddWithValue("@situation", Beat("situation"));
                    cmd.Parameters.AddWithValue("@task", Beat("task"));
                    cmd.Parameters.AddWithValue("@action", Beat("action"));
                    cmd.Parameters.AddWithValue("@result", Beat("result"));
                    cmd.Parameters.AddWithValue("@context", context);
                    cmd.Parameters.AddWithValue("@status", status);
                    cmd.ExecuteNonQuery();
                }

                ClearLinks(conn, tx, id);

                foreach (var s in skills.Where(s => !string.IsNullOrWhiteSpace(s.Name)))
                    LinkSkill(conn, tx, id, s.Name.Trim(), string.IsNullOrWhiteSpace(s.Kind) ? "technical" : s.Kind.Trim());
                foreach (var t in tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct())
                    LinkTag(conn, tx, id, t);
                foreach (var m in metrics.Where(m => !string.IsNullOrWhiteSpace(m.Label)))
                    InsertMetric(conn, tx, id, m);

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT OR IGNORE INTO embed_queue (experience_id, enqueued_at) VALUES (@id, datetime('now'))";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return (id, "", null);
            }
            catch (SqliteException ex)
            {
                return ("", "", ex.Message);
            }
        }
    }

    static IReadOnlyDictionary<string, string> ConfidenceFor(
        string status, string situation, string task, string action, string result)
    {
        var present = status.Equals("confirmed", StringComparison.OrdinalIgnoreCase) ? "high" : "medium";
        string Level(string beat) => string.IsNullOrWhiteSpace(beat) ? "low" : present;
        return new Dictionary<string, string>
        {
            ["situation"] = Level(situation), ["task"] = Level(task),
            ["action"] = Level(action), ["result"] = Level(result),
        };
    }

    static DateTime ParseUtc(string? text)
        => DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt) ? dt : DateTime.MinValue;

    static void ClearLinks(SqliteConnection conn, SqliteTransaction tx, string id)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM experience_skills WHERE experience_id = @id",
            "DELETE FROM experience_tags WHERE experience_id = @id",
            "DELETE FROM metrics WHERE experience_id = @id",
        })
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    static void LinkSkill(SqliteConnection conn, SqliteTransaction tx, string experienceId, string name, string kind)
    {
        string skillId;
        using (var upsert = conn.CreateCommand())
        {
            upsert.Transaction = tx;
            upsert.CommandText = "INSERT OR IGNORE INTO skills (id, name, kind) VALUES (@id, @name, @kind)";
            upsert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            upsert.Parameters.AddWithValue("@name", name);
            upsert.Parameters.AddWithValue("@kind", kind);
            upsert.ExecuteNonQuery();
        }
        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT id FROM skills WHERE name = @name";
            sel.Parameters.AddWithValue("@name", name);
            skillId = (string)sel.ExecuteScalar()!;
        }
        using var link = conn.CreateCommand();
        link.Transaction = tx;
        link.CommandText = "INSERT OR IGNORE INTO experience_skills (experience_id, skill_id) VALUES (@exp, @skill)";
        link.Parameters.AddWithValue("@exp", experienceId);
        link.Parameters.AddWithValue("@skill", skillId);
        link.ExecuteNonQuery();
    }

    static void LinkTag(SqliteConnection conn, SqliteTransaction tx, string experienceId, string name)
    {
        string tagId;
        using (var upsert = conn.CreateCommand())
        {
            upsert.Transaction = tx;
            upsert.CommandText = "INSERT OR IGNORE INTO tags (id, name) VALUES (@id, @name)";
            upsert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            upsert.Parameters.AddWithValue("@name", name);
            upsert.ExecuteNonQuery();
        }
        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT id FROM tags WHERE name = @name";
            sel.Parameters.AddWithValue("@name", name);
            tagId = (string)sel.ExecuteScalar()!;
        }
        using var link = conn.CreateCommand();
        link.Transaction = tx;
        link.CommandText = "INSERT OR IGNORE INTO experience_tags (experience_id, tag_id) VALUES (@exp, @tag)";
        link.Parameters.AddWithValue("@exp", experienceId);
        link.Parameters.AddWithValue("@tag", tagId);
        link.ExecuteNonQuery();
    }

    static void InsertMetric(SqliteConnection conn, SqliteTransaction tx, string experienceId, VaultMetric m)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO metrics (id, experience_id, label, value, unit) VALUES (@id, @exp, @label, @value, @unit)";
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@exp", experienceId);
        cmd.Parameters.AddWithValue("@label", m.Label);
        cmd.Parameters.AddWithValue("@value", (object?)m.Value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@unit", (object?)m.Unit ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    static Dictionary<string, List<VaultSkill>> GroupSkills(SqliteConnection conn)
        => Group(conn, "SELECT experience_id, skill_name, skill_kind FROM v_experience_skills",
            r => new VaultSkill(r.GetString(1), r.IsDBNull(2) ? "technical" : r.GetString(2)));

    static Dictionary<string, List<string>> GroupStrings(SqliteConnection conn, string sql)
        => Group(conn, sql, r => r.GetString(1));

    static Dictionary<string, List<VaultMetric>> GroupMetrics(SqliteConnection conn)
        => Group(conn, "SELECT experience_id, label, value, unit FROM v_experience_metrics",
            r => new VaultMetric(r.GetString(1), r.IsDBNull(2) ? null : r.GetDouble(2), r.IsDBNull(3) ? null : r.GetString(3)));

    static Dictionary<string, List<string>> GroupEntities(SqliteConnection conn)
        => Group(conn,
            "SELECT g.src_id, e.name FROM v_edges g JOIN v_entities e ON e.id = g.dst_id " +
            "WHERE g.src_kind = 'experience' AND g.dst_kind = 'entity'",
            r => $"[[{r.GetString(1)}]]");

    static Dictionary<string, List<T>> Group<T>(SqliteConnection conn, string sql, Func<SqliteDataReader, T> project)
    {
        var map = new Dictionary<string, List<T>>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read()) Add(map, r.GetString(0), project(r));
        return map;
    }

    static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
        => (map.TryGetValue(key, out var list) ? list : map[key] = []).Add(value);
}
