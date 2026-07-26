using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Recall_Seed.Scroll;

/// <summary>Read the Scroll endpoint-spawner grading verdicts received over the loopback callback.</summary>
[McpServerToolType]
internal static class ScrollVerdictTools
{
    [McpServerTool(Name = "get_scroll_verdict")]
    [Description("Return the most recent Scroll endpoint-spawner grading verdict(s) delivered to this " +
                 "server over the loopback callback. Scroll POSTs a verdict — status (pass|fail|tle|error) " +
                 "with passed/total counts and a timestamp — when a spawned coding endpoint is submitted. " +
                 "Pass the spawnId returned by seed_ide_endpoint to get just that problem's latest verdict " +
                 "(the endpoint id is browser-minted and opaque, so spawnId is the correlation key); " +
                 "otherwise get the newest verdicts across all endpoints. Returns an empty list if the " +
                 "receiver is disabled (SCROLL_VERDICT_PORT unset) or nothing has been received yet.")]
    public static ScrollVerdictResult GetScrollVerdict(
        [Description("Optional: the spawnId from seed_ide_endpoint; returns only that problem's latest verdict.")] string? spawnId = null,
        [Description("Max verdicts to return, newest first (1-100, default 10). Ignored when spawnId is set.")] int limit = 10)
    {
        if (!string.IsNullOrWhiteSpace(spawnId))
        {
            var latest = VerdictLog.Shared.LatestByCorrelation(spawnId!.Trim());
            return new ScrollVerdictResult(latest is null ? [] : [View(latest)]);
        }

        var items = VerdictLog.Shared.Recent(Math.Clamp(limit, 1, 100)).Select(View).ToList();
        return new ScrollVerdictResult(items);
    }

    static ScrollVerdictView View(ScrollVerdict v)
        => new(v.Correlation, v.EndpointId, v.Status, v.WithinBudget, v.Passed, v.Total, v.At);
}

internal sealed record ScrollVerdictResult(IReadOnlyList<ScrollVerdictView> Verdicts, string? Error = null);

internal sealed record ScrollVerdictView(
    string? SpawnId, string EndpointId, string Status, bool WithinBudget, int Passed, int Total, long At);
