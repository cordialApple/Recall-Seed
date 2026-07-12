using PersonalServer.Assistant;

namespace PersonalServer.Tests;

public class AssistantToolTests : IDisposable
{
    readonly string _dir;
    readonly string _loopbackFile;

    public AssistantToolTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "psrv-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _loopbackFile = Path.Combine(_dir, "loopback.json");
    }

    void PointAt(int port)
        => File.WriteAllText(_loopbackFile, $$"""{"port": {{port}}, "token": "test-token"}""");

    [Fact]
    public async Task RetrieveSemantic_proxies_results_from_loopback()
    {
        using var stub = new StubLoopback(path => path == "/retrieve"
            ? """{"results":[{"experience_id":"a","title":"Led migration","score":0.91,"snippet":"…cutover…"}]}"""
            : "{}");
        PointAt(stub.Port);
        Environment.SetEnvironmentVariable(Loopback.FileEnvVar, _loopbackFile);

        var res = await RetrieveTools.RetrieveSemantic("led under pressure");

        Assert.Null(res.Error);
        var hit = Assert.Single(res.Results);
        Assert.Equal("a", hit.ExperienceId);
        Assert.Equal("Led migration", hit.Title);
        Assert.Equal(0.91, hit.Score, 3);
        Assert.Contains("cutover", hit.Snippet);
    }

    [Fact]
    public async Task GenerateStory_proxies_story_and_passes_provenance()
    {
        using var stub = new StubLoopback(path => path == "/generate"
            ? """{"story":"When the payments platform went down, I…","experience_ids":["a","b"]}"""
            : "{}");
        PointAt(stub.Port);
        Environment.SetEnvironmentVariable(Loopback.FileEnvVar, _loopbackFile);

        var res = await StoryTools.GenerateStory(["a", "b"], genre: "behavioral");

        Assert.Null(res.Error);
        Assert.Contains("payments platform", res.Story);
        Assert.Equal(["a", "b"], res.ExperienceIds);
    }

    [Fact]
    public async Task RetrieveSemantic_returns_not_running_when_no_loopback_file()
    {
        Environment.SetEnvironmentVariable(Loopback.FileEnvVar, Path.Combine(_dir, "absent.json"));
        var res = await RetrieveTools.RetrieveSemantic("anything");
        Assert.Equal(Loopback.NotRunning, res.Error);
        Assert.Empty(res.Results);
    }

    [Fact]
    public async Task GenerateStory_returns_not_running_when_server_down()
    {
        int deadPort;
        using (var probe = new StubLoopback(_ => "{}")) deadPort = probe.Port;
        PointAt(deadPort);
        Environment.SetEnvironmentVariable(Loopback.FileEnvVar, _loopbackFile);

        var res = await StoryTools.GenerateStory(["a"]);
        Assert.Equal(Loopback.NotRunning, res.Error);
        Assert.Null(res.Story);
    }

    [Fact]
    public async Task Tools_validate_empty_input_without_calling_loopback()
    {
        Environment.SetEnvironmentVariable(Loopback.FileEnvVar, null);
        Assert.Equal("query must not be empty", (await RetrieveTools.RetrieveSemantic("  ")).Error);
        Assert.Equal("experience_ids must not be empty", (await StoryTools.GenerateStory([])).Error);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Loopback.FileEnvVar, null);
        try { Directory.Delete(_dir, true); } catch { }
    }
}
