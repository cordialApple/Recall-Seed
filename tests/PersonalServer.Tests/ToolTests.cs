using PersonalServer.Data;
using PersonalServer.KnowledgeGraph;

namespace PersonalServer.Tests;

public class ToolTests : IDisposable
{
    readonly string _dir;

    public ToolTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "psrv-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        var path = Fixture.Create(_dir);
        Environment.SetEnvironmentVariable(Db.PathEnvVar, path);
    }

    [Fact]
    public void GetSchema_reports_all_views_present()
    {
        var s = SchemaTools.GetSchema();
        Assert.Null(s.Error);
        Assert.Empty(s.MissingViews);
        Assert.Contains("mentions", s.EdgeRelations);
        Assert.Contains("query", s.ReadTools);
        Assert.Contains("capture_experience", s.WriteTools);
        Assert.Equal(6, s.EntityKinds.Count);
    }

    [Fact]
    public void Query_by_skill_returns_matching_experiences_with_hydration()
    {
        var res = QueryTools.Query(skills: ["sql"]);
        Assert.Null(res.Error);
        var ids = res.Experiences.Select(e => e.Id).OrderBy(x => x).ToArray();
        Assert.Equal(["a", "b"], ids);

        var a = res.Experiences.Single(e => e.Id == "a");
        Assert.Contains("leadership", a.Skills);
        Assert.Contains("backend", a.Tags);
        Assert.Single(a.Metrics);
        Assert.Equal("downtime", a.Metrics[0].Label);
    }

    [Fact]
    public void Query_by_context_and_status_filter()
    {
        Assert.Equal(["c"], QueryTools.Query(context: "project").Experiences.Select(e => e.Id).ToArray());
        Assert.Equal(["a", "b"],
            QueryTools.Query(status: "confirmed").Experiences.Select(e => e.Id).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void SearchNodes_finds_experience_by_fts()
    {
        var res = SearchTools.SearchNodes("migration");
        Assert.Null(res.Error);
        var hit = Assert.Single(res.Nodes, n => n.NodeType == "experience");
        Assert.Equal("a", hit.Id);
        Assert.Contains("[migration]", hit.Snippet);
    }

    [Fact]
    public void SearchNodes_finds_entity_by_name_and_kind_filter()
    {
        var res = SearchTools.SearchNodes("Postgre", kinds: ["tool"]);
        Assert.Null(res.Error);
        var hit = Assert.Single(res.Nodes);
        Assert.Equal("tool", hit.NodeType);
        Assert.Equal("PostgreSQL", hit.Title);
    }

    [Fact]
    public void Neighbors_returns_linked_entities_and_shared_connections()
    {
        var res = NeighborTools.Neighbors("a");
        Assert.Null(res.Error);
        Assert.Equal(["Payments Platform", "PostgreSQL"], res.Entities.Select(e => e.Name).OrderBy(x => x).ToArray());

        var b = Assert.Single(res.Connections);
        Assert.Equal("b", b.ExperienceId);
        Assert.Contains("Payments Platform", b.ViaEntities);
        Assert.Contains("leadership", b.ViaSkills);
        Assert.Contains("sql", b.ViaSkills);
    }

    [Fact]
    public void Neighbors_unknown_id_returns_error()
    {
        var res = NeighborTools.Neighbors("nope");
        Assert.NotNull(res.Error);
        Assert.Contains("nope", res.Error);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Db.PathEnvVar, null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
