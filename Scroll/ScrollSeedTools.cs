using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Recall_Seed.Scroll;

/// <summary>
/// Seeds a programmatic (Claude-only) ide-es coding problem into Scroll and returns a spawn URL. This is
/// the whole PersonalServer side of contract 2: build+validate the schema, bake in a correlation-bearing
/// loopback resultCallbackUrl, encode, and hand back a URL. No editor, no grader, no sandbox lives here.
/// Scroll owns spawning, hosting, and grading. programmatic is forced to 'on' (bare seed-and-grade).
/// </summary>
[McpServerToolType]
internal static class ScrollSeedTools
{
    [McpServerTool(Name = "seed_ide_endpoint")]
    [Description("Seed a coding problem into Scroll's ide-es endpoint spawner and get back a URL to hand " +
                 "the user in conversation. You author the problem; Scroll hosts the editor and runs the " +
                 "hidden-test grader. Requires: title, problem statement, goalCondition (what 'done' means), " +
                 "functionName (the JS function the grader calls), tests (at least one MUST be hidden, the " +
                 "hidden set is the graded oracle; add an adversarially large hidden input to force the " +
                 "efficient solution), and tleBudgetMs (per-test wall-clock budget that gates brute force). " +
                 "Optional: complexityBudget (informational, e.g. 'O(n log n)') and staged hints. Returns a " +
                 "spawnUrl and a spawnId; after the user submits, read the pass/fail outcome with " +
                 "get_scroll_verdict(spawnId). The verdict only comes back if the loopback receiver is on " +
                 "(SCROLL_VERDICT_PORT set); otherwise callbackWired is false and no verdict will arrive.")]
    public static SeedResult SeedIdeEndpoint(
        [Description("Display title for the problem surface.")] string title,
        [Description("The problem statement the user works against.")] string problem,
        [Description("Goal condition: what 'done' means for this endpoint.")] string goalCondition,
        [Description("Name of the JavaScript function the grader invokes (a valid JS identifier).")] string functionName,
        [Description("Test cases. At least one must have hidden=true (the graded oracle).")] SeedTestCase[] tests,
        [Description("Per-test wall-clock budget in ms; a positive number. Gates 'use the efficient strategy'.")] double tleBudgetMs,
        [Description("Optional informational complexity target, e.g. 'O(n log n)'. Not machine-enforced.")] string? complexityBudget = null,
        [Description("Optional staged strategy hints, each with text and an optional afterFailedAttempts count.")] SeedHint[]? hints = null,
        [Description("'consumer' (ephemeral endpoint, default) or 'human' (durable, user-owned).")] string lifecycleOwner = "consumer")
        => Build(ScrollSpawnConfig.ResolveBaseUrl(), ScrollSpawnConfig.ReceiverPort(), Guid.NewGuid().ToString("N"),
            title, problem, goalCondition, functionName, tests, tleBudgetMs, complexityBudget, hints, lifecycleOwner);

    /// <summary>Pure core: resolved config + correlation in, result out, no env, no clock, no randomness.</summary>
    internal static SeedResult Build(
        string? baseUrl, int? receiverPort, string correlationId,
        string title, string problem, string goalCondition, string functionName,
        SeedTestCase[] tests, double tleBudgetMs,
        string? complexityBudget = null, SeedHint[]? hints = null, string lifecycleOwner = "consumer")
    {
        if (baseUrl is null)
            return SeedResult.Fail($"{ScrollSpawnConfig.BaseUrlEnv} is set but is not an http(s) origin");

        if (tests is null || tests.Length == 0)
            return SeedResult.Fail("tests must be a non-empty array with at least one hidden test");

        var callbackUrl = receiverPort is null ? null : ScrollSpawnConfig.CallbackUrl(receiverPort.Value, correlationId);

        var schema = new IdeEsSchema(
            SchemaVersion: IdeEsSchema.Version,
            Kind: IdeEsSchema.KindIdeEs,
            Title: title ?? "",
            LifecycleOwner: string.IsNullOrWhiteSpace(lifecycleOwner) ? "consumer" : lifecycleOwner.Trim(),
            Programmatic: "on",
            GoalCondition: goalCondition ?? "",
            Problem: problem ?? "",
            Entry: new IdeEsEntry(IdeEsSchema.EntryLanguage, functionName ?? ""),
            Tests: tests.Select(t => new IdeEsTest(t.Input ?? "", t.ExpectedOutput ?? "", t.Hidden,
                string.IsNullOrWhiteSpace(t.Name) ? null : t.Name)).ToList(),
            TleBudgetMs: tleBudgetMs,
            ComplexityBudget: string.IsNullOrWhiteSpace(complexityBudget) ? null : complexityBudget,
            Hints: hints is null || hints.Length == 0
                ? null
                : hints.Select(h => new IdeEsHint(h.Text ?? "", h.AfterFailedAttempts)).ToList(),
            ResultCallbackUrl: callbackUrl);

        var errors = schema.Validate();
        if (errors.Count > 0) return SeedResult.Fail(errors);

        var param = schema.EncodeParam();
        if (param.Length > IdeEsSchema.MaxParamLen)
            return SeedResult.Fail($"encoded problem is {param.Length} chars, over Scroll's {IdeEsSchema.MaxParamLen} URL limit; shorten it");

        return new SeedResult(
            SpawnUrl: ScrollSpawnConfig.SpawnUrl(baseUrl, param),
            SpawnId: callbackUrl is null ? null : correlationId,
            CallbackWired: callbackUrl is not null);
    }
}

internal sealed record SeedTestCase(string Input, string ExpectedOutput, bool Hidden, string? Name = null);

internal sealed record SeedHint(string Text, double? AfterFailedAttempts = null);

internal sealed record SeedResult(
    string? SpawnUrl, string? SpawnId, bool CallbackWired,
    string Programmatic = "on", IReadOnlyList<string>? Errors = null)
{
    public static SeedResult Fail(string error) => Fail([error]);
    public static SeedResult Fail(IReadOnlyList<string> errors) => new(null, null, false, "on", errors);
}
