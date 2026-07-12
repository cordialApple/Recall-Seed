using Microsoft.Data.Sqlite;
using PersonalServer.Capture;
using PersonalServer.Data;
using PersonalServer.KnowledgeGraph;

namespace PersonalServer.Tests;

public class WriteToolTests : IDisposable
{
    readonly string _dir;
    readonly string _path;

    public WriteToolTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "psrv-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Fixture.Create(_dir);
        Environment.SetEnvironmentVariable(Db.PathEnvVar, _path);
    }

    [Fact]
    public void AddEntity_is_idempotent_and_dedups()
    {
        var first = EntityTools.AddEntity("tool", "Kubernetes");
        Assert.Null(first.Error);
        Assert.NotNull(first.Id);

        var again = EntityTools.AddEntity("tool", "Kubernetes");
        Assert.Equal(first.Id, again.Id);

        var seeded = EntityTools.AddEntity("project", "Payments Platform");
        Assert.Equal("p1", seeded.Id);
    }

    [Fact]
    public void AddEntity_rejects_unknown_kind()
    {
        var res = EntityTools.AddEntity("planet", "Mars");
        Assert.Null(res.Id);
        Assert.Contains("invalid kind", res.Error);
    }

    [Fact]
    public void AddEdge_rejects_dangling_endpoint()
    {
        var res = EntityTools.AddEdge("experience", "ghost", "mentions", "entity", "p1");
        Assert.Null(res.Id);
        Assert.Contains("source node not found", res.Error);

        var res2 = EntityTools.AddEdge("experience", "a", "mentions", "entity", "ghost");
        Assert.Null(res2.Id);
        Assert.Contains("destination node not found", res2.Error);
    }

    [Fact]
    public void AddEdge_is_idempotent()
    {
        var existing = EntityTools.AddEdge("experience", "a", "mentions", "entity", "p1");
        Assert.Equal("e1", existing.Id);

        var made = EntityTools.AddEdge("experience", "a", "worked_on", "entity", "t1");
        Assert.Null(made.Error);
        Assert.NotNull(made.Id);
        var again = EntityTools.AddEdge("experience", "a", "worked_on", "entity", "t1");
        Assert.Equal(made.Id, again.Id);
    }

    [Fact]
    public void CaptureExperience_appears_in_query_search_and_embed_queue()
    {
        var res = ExperienceTools.CaptureExperience(
            title: "Ran a Zeppelin migration",
            action: "coordinated the Kubernetes cutover",
            context: "work",
            skills: ["kubernetes", "leadership"],
            tags: ["devops"],
            metrics: [new MetricInput("uptime", 99.9, "%")]);

        Assert.Null(res.Error);
        Assert.NotNull(res.Id);

        var bySkill = QueryTools.Query(skills: ["kubernetes"]);
        var card = Assert.Single(bySkill.Experiences, e => e.Id == res.Id);
        Assert.Equal("draft", card.Status);
        Assert.Contains("leadership", card.Skills);
        Assert.Contains("devops", card.Tags);
        Assert.Equal("uptime", Assert.Single(card.Metrics).Label);

        var found = SearchTools.SearchNodes("Zeppelin");
        Assert.Contains(found.Nodes, n => n.NodeType == "experience" && n.Id == res.Id);

        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM embed_queue WHERE experience_id = @id";
        cmd.Parameters.AddWithValue("@id", res.Id);
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void CaptureExperience_requires_a_star_field()
    {
        var res = ExperienceTools.CaptureExperience(title: "Empty");
        Assert.Null(res.Id);
        Assert.Contains("STAR field", res.Error);
    }

    [Fact]
    public void CaptureExperience_rejects_bad_status()
    {
        var res = ExperienceTools.CaptureExperience(title: "x", action: "did a thing", status: "published");
        Assert.Null(res.Id);
        Assert.Contains("invalid status", res.Error);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Db.PathEnvVar, null);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
