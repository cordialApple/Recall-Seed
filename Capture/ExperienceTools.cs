using System.ComponentModel;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using PersonalServer.Data;

namespace PersonalServer.Capture;

/// <summary>Captures a new STAR experience back into the bank and enqueues it for embedding.</summary>
[McpServerToolType]
internal static class ExperienceTools
{
    static readonly string[] Contexts = ["work", "project", "class", "other"];
    static readonly string[] Statuses = ["draft", "confirmed"];

    [McpServerTool(Name = "capture_experience")]
    [Description("Capture a new experience into the STARfolio bank: a titled STAR record " +
                 "(situation / task / action / result) with optional skills, tags, and metrics. " +
                 "Shape a good draft in conversation first — this call is the commit step. Defaults " +
                 "status to 'draft'. Keyword and structured search see it immediately; semantic " +
                 "search includes it after STARfolio's next launch drains the embed queue.")]
    public static CaptureResult CaptureExperience(
        [Description("Short title for the experience.")] string title,
        [Description("Situation: the context/background.")] string? situation = null,
        [Description("Task: what needed to be done.")] string? task = null,
        [Description("Action: what you did.")] string? action = null,
        [Description("Result: the outcome, ideally quantified.")] string? result = null,
        [Description("Context: work, project, class, or other (default work).")] string context = "work",
        [Description("Skill names to link (created if new).")] string[]? skills = null,
        [Description("Tag names to link (created if new).")] string[]? tags = null,
        [Description("Metrics to attach (label, optional value, optional unit).")] MetricInput[]? metrics = null,
        [Description("Status: draft or confirmed (default draft).")] string status = "draft")
    {
        if (string.IsNullOrWhiteSpace(title))
            return new CaptureResult(null, Error: "title must not be empty");
        if (string.IsNullOrWhiteSpace(situation) && string.IsNullOrWhiteSpace(task)
            && string.IsNullOrWhiteSpace(action) && string.IsNullOrWhiteSpace(result))
            return new CaptureResult(null, Error: "at least one STAR field (situation, task, action, result) is required");
        if (!Contexts.Contains(context))
            return new CaptureResult(null, Error: $"invalid context '{context}'; must be one of: {string.Join(", ", Contexts)}");
        if (!Statuses.Contains(status))
            return new CaptureResult(null, Error: $"invalid status '{status}'; must be one of: {string.Join(", ", Statuses)}");

        var (conn, error) = WriteGuard.OpenReadWrite();
        if (error != null) return new CaptureResult(null, Error: error);

        using (conn)
        {
            using var tx = conn!.BeginTransaction();
            try
            {
                var id = Guid.NewGuid().ToString();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "INSERT INTO experiences (id, title, situation, task, action, result_text, context, status, created_at, updated_at) " +
                        "VALUES (@id, @title, @situation, @task, @action, @result, @context, @status, datetime('now'), datetime('now'))";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@title", title);
                    cmd.Parameters.AddWithValue("@situation", situation ?? "");
                    cmd.Parameters.AddWithValue("@task", task ?? "");
                    cmd.Parameters.AddWithValue("@action", action ?? "");
                    cmd.Parameters.AddWithValue("@result", result ?? "");
                    cmd.Parameters.AddWithValue("@context", context);
                    cmd.Parameters.AddWithValue("@status", status);
                    cmd.ExecuteNonQuery();
                }

                foreach (var skill in Clean(skills))
                    Link(conn, tx, "skills", "experience_skills", "skill_id", id, skill);
                foreach (var tag in Clean(tags))
                    Link(conn, tx, "tags", "experience_tags", "tag_id", id, tag);

                foreach (var m in (metrics ?? []).Where(m => !string.IsNullOrWhiteSpace(m.Label)))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "INSERT INTO metrics (id, experience_id, label, value, unit) VALUES (@id, @exp, @label, @value, @unit)";
                    cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                    cmd.Parameters.AddWithValue("@exp", id);
                    cmd.Parameters.AddWithValue("@label", m.Label);
                    cmd.Parameters.AddWithValue("@value", (object?)m.Value ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@unit", (object?)m.Unit ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT OR IGNORE INTO embed_queue (experience_id, enqueued_at) VALUES (@id, datetime('now'))";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return new CaptureResult(id,
                    Note: "Captured. Keyword and structured search include it now; semantic search after STARfolio's next launch.");
            }
            catch (SqliteException ex)
            {
                return new CaptureResult(null, Error: ex.Message);
            }
        }
    }

    static IEnumerable<string> Clean(string[]? values)
        => (values ?? []).Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).Distinct();

    static void Link(SqliteConnection conn, SqliteTransaction tx, string dimTable, string joinTable, string fkColumn, string experienceId, string name)
    {
        string dimId;
        using (var upsert = conn.CreateCommand())
        {
            upsert.Transaction = tx;
            upsert.CommandText = $"INSERT OR IGNORE INTO {dimTable} (id, name) VALUES (@id, @name)";
            upsert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            upsert.Parameters.AddWithValue("@name", name);
            upsert.ExecuteNonQuery();
        }
        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = $"SELECT id FROM {dimTable} WHERE name = @name";
            sel.Parameters.AddWithValue("@name", name);
            dimId = (string)sel.ExecuteScalar()!;
        }
        using var link = conn.CreateCommand();
        link.Transaction = tx;
        link.CommandText = $"INSERT OR IGNORE INTO {joinTable} (experience_id, {fkColumn}) VALUES (@exp, @dim)";
        link.Parameters.AddWithValue("@exp", experienceId);
        link.Parameters.AddWithValue("@dim", dimId);
        link.ExecuteNonQuery();
    }
}
