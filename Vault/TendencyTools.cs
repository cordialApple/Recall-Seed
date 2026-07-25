using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Recall_Seed.Vault;

/// <summary>Read across the whole vault to surface tendencies in how the person works.</summary>
[McpServerToolType]
internal static class TendencyTools
{
    [McpServerTool(Name = "find_tendencies")]
    [Description("Compute real aggregates across the entire experience vault — skill/tag/context/entity " +
                 "frequencies, metric coverage, open gaps, draft vs confirmed — and return them with the " +
                 "grounded tendency-analysis prompt. The counts are ground truth; interpret them into " +
                 "patterns in how the person works (recurring strengths, dominant contexts, where results " +
                 "are unquantified) without inventing or adjusting any number.")]
    public static TendencyResult FindTendencies()
    {
        var (items, error) = ExperienceStore.Current.LoadAll();
        if (error != null) return new TendencyResult(Prompts.Tendencies, Empty(), error);
        if (items.Count == 0)
            return new TendencyResult(Prompts.Tendencies, Empty(), "the vault has no experiences yet; bank some first");

        var stats = new TendencyStats(
            ExperienceCount: items.Count,
            Confirmed: items.Count(e => e.Status == "confirmed"),
            Draft: items.Count(e => e.Status != "confirmed"),
            WithMetrics: items.Count(e => e.Metrics.Count > 0),
            WithoutMetrics: items.Count(e => e.Metrics.Count == 0),
            OpenGaps: items.Sum(e => e.Gaps.Count),
            Skills: Rank(items.SelectMany(e => e.Skills.Select(s => s.Name))),
            SkillKinds: Rank(items.SelectMany(e => e.Skills.Select(s => s.Kind))),
            Tags: Rank(items.SelectMany(e => e.Tags)),
            Contexts: Rank(items.Select(e => e.Context)),
            Entities: Rank(items.SelectMany(e => e.Entities)));

        return new TendencyResult(Prompts.Tendencies, stats);
    }

    static IReadOnlyList<NameCount> Rank(IEnumerable<string> values)
        => values.Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NameCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    static TendencyStats Empty() => new(0, 0, 0, 0, 0, 0, [], [], [], [], []);
}
