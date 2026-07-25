using System.Text;
using Recall_Seed.Scroll;

namespace Recall_Seed.Tests;

public class ScrollVerdictParseTests
{
    static bool Parse(string json, out ScrollVerdict? v, out string? err)
        => ScrollVerdict.TryParse(Encoding.UTF8.GetBytes(json), out v, out err);

    // The four canonical wire samples from the Scroll repo's result-payload.golden.json.
    [Theory]
    [InlineData("{\"endpointId\":\"ep_golden\",\"status\":\"pass\",\"withinBudget\":true,\"passed\":2,\"total\":2,\"at\":1700000000000}", "pass", true, 2)]
    [InlineData("{\"endpointId\":\"ep_golden\",\"status\":\"fail\",\"withinBudget\":true,\"passed\":1,\"total\":2,\"at\":1700000000000}", "fail", true, 1)]
    [InlineData("{\"endpointId\":\"ep_golden\",\"status\":\"tle\",\"withinBudget\":false,\"passed\":1,\"total\":2,\"at\":1700000000000}", "tle", false, 1)]
    [InlineData("{\"endpointId\":\"ep_golden\",\"status\":\"error\",\"withinBudget\":true,\"passed\":0,\"total\":2,\"at\":1700000000000}", "error", true, 0)]
    public void Accepts_every_golden_vector(string json, string status, bool withinBudget, int passed)
    {
        Assert.True(Parse(json, out var v, out var err), err);
        Assert.NotNull(v);
        Assert.Equal("ep_golden", v!.EndpointId);
        Assert.Equal(status, v.Status);
        Assert.Equal(withinBudget, v.WithinBudget);
        Assert.Equal(passed, v.Passed);
        Assert.Equal(2, v.Total);
        Assert.Equal(1700000000000L, v.At);
    }

    [Fact]
    public void Tolerates_unknown_extra_fields_forward_compat()
    {
        Assert.True(Parse("{\"endpointId\":\"e\",\"status\":\"pass\",\"withinBudget\":true,\"passed\":1,\"total\":1,\"at\":1,\"future\":\"x\"}", out var v, out _));
        Assert.Equal("e", v!.EndpointId);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("\"str\"")]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("{\"status\":\"pass\",\"withinBudget\":true,\"passed\":1,\"total\":1,\"at\":1}")]       // no endpointId
    [InlineData("{\"endpointId\":\"\",\"status\":\"pass\",\"withinBudget\":true,\"passed\":1,\"total\":1,\"at\":1}")] // empty id
    [InlineData("{\"endpointId\":\"e\",\"status\":\"skipped\",\"withinBudget\":true,\"passed\":1,\"total\":1,\"at\":1}")] // bad enum
    [InlineData("{\"endpointId\":\"e\",\"status\":\"pass\",\"withinBudget\":\"true\",\"passed\":1,\"total\":1,\"at\":1}")] // bool as string
    [InlineData("{\"endpointId\":\"e\",\"status\":\"pass\",\"withinBudget\":true,\"passed\":-1,\"total\":1,\"at\":1}")]    // passed < 0
    [InlineData("{\"endpointId\":\"e\",\"status\":\"pass\",\"withinBudget\":true,\"passed\":1,\"total\":0,\"at\":1}")]     // total < 1
    [InlineData("{\"endpointId\":\"e\",\"status\":\"pass\",\"withinBudget\":true,\"passed\":1,\"total\":1,\"at\":\"x\"}")] // at as string
    [InlineData("{\"endpointId\":\"e\",\"status\":\"pass\",\"withinBudget\":true,\"passed\":1,\"total\":1}")]             // no at
    public void Rejects_malformed_or_out_of_contract_payloads(string json)
    {
        Assert.False(Parse(json, out var v, out var err));
        Assert.Null(v);
        Assert.NotNull(err);
    }
}

public class VerdictLogTests
{
    static ScrollVerdict V(string id) => new(id, "pass", true, 1, 1, 1);

    [Fact]
    public void Evicts_oldest_past_capacity()
    {
        var log = new VerdictLog(capacity: 3);
        foreach (var i in Enumerable.Range(0, 5)) log.Add(V($"e{i}"));
        Assert.Equal(3, log.Count);
        var recent = log.Recent(10);
        Assert.Equal(new[] { "e4", "e3", "e2" }, recent.Select(v => v.EndpointId).ToArray()); // newest first
        Assert.Null(log.Latest("e0"));                                                          // evicted
    }

    [Fact]
    public void Recent_respects_limit_and_Latest_filters_by_endpoint()
    {
        var log = new VerdictLog();
        log.Add(V("a"));
        log.Add(new ScrollVerdict("a", "fail", true, 0, 1, 2));
        log.Add(V("b"));
        Assert.Single(log.Recent(1));
        Assert.Equal("b", log.Recent(1)[0].EndpointId);
        Assert.Equal("fail", log.Latest("a")!.Status); // newest for 'a'
        Assert.Equal("b", log.Latest()!.EndpointId);   // newest overall
        Assert.Null(log.Latest("missing"));
    }
}

public class ScrollReceiverConfigTests
{
    static int? Resolve(string? raw) =>
        ScrollReceiverConfig.ResolvePort(new Dictionary<string, string?> { [ScrollReceiverConfig.PortEnv] = raw });

    [Fact]
    public void Off_by_default_and_on_valid_port()
    {
        Assert.Null(Resolve(null));
        Assert.Null(Resolve(""));
        Assert.Null(Resolve("  "));
        Assert.Null(Resolve("nope"));
        Assert.Null(Resolve("0"));
        Assert.Null(Resolve("70000"));
        Assert.Equal(5599, Resolve("5599"));
        Assert.Equal(5599, Resolve(" 5599 "));
    }
}

public class ScrollVerdictToolTests
{
    [Fact]
    public void get_scroll_verdict_reads_the_shared_log_by_endpoint()
    {
        var id = "ep-tool-" + Guid.NewGuid().ToString("N");
        VerdictLog.Shared.Add(new ScrollVerdict(id, "tle", false, 3, 4, 1700000000001));

        var byId = ScrollVerdictTools.GetScrollVerdict(id);
        var v = Assert.Single(byId.Verdicts);
        Assert.Equal(id, v.EndpointId);
        Assert.Equal("tle", v.Status);
        Assert.False(v.WithinBudget);
        Assert.Equal(3, v.Passed);
        Assert.Equal(4, v.Total);

        Assert.Contains(ScrollVerdictTools.GetScrollVerdict(limit: 100).Verdicts, x => x.EndpointId == id);
        Assert.Empty(ScrollVerdictTools.GetScrollVerdict("ep-none-" + Guid.NewGuid().ToString("N")).Verdicts);
    }
}
