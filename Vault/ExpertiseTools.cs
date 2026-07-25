using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Recall_Seed.Vault;

/// <summary>Summarize what the vault provably demonstrates about the person's expertise.</summary>
[McpServerToolType]
internal static class ExpertiseTools
{
    [McpServerTool(Name = "expertise_profile")]
    [Description("Build an evidence-grounded expertise profile from the vault: each skill weighted by " +
                 "how many experiences evidence it (with their ids), the experiences carrying concrete " +
                 "metrics (the strongest, most defensible results), and the recurring domains/tags. " +
                 "Returns the aggregates plus the grounded prompt — describe only what the evidence backs, " +
                 "preserve metrics verbatim, and be honest about thinly-evidenced skills.")]
    public static ExpertiseResult ExpertiseProfile()
    {
        var (items, error) = ExperienceStore.Current.LoadAll();
        if (error != null) return new ExpertiseResult(Prompts.Expertise, [], [], [], error);
        if (items.Count == 0)
            return new ExpertiseResult(Prompts.Expertise, [], [], [], "the vault has no experiences yet; bank some first");

        var skills = items
            .SelectMany(e => e.Skills.Select(s => (s.Name, s.Kind, e.Id)))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var ids = g.Select(x => x.Id).Distinct().ToList();
                return new SkillEvidence(
                    g.Key,
                    g.Select(x => x.Kind).GroupBy(k => k).OrderByDescending(k => k.Count()).First().Key,
                    ids.Count,
                    ids);
            })
            .OrderByDescending(s => s.Count).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var strongest = items
            .Where(e => e.Metrics.Count > 0)
            .OrderByDescending(e => e.Metrics.Count)
            .Select(e => new ExperienceRef(e.Id, e.Title, e.Metrics))
            .ToList();

        var domains = items.SelectMany(e => e.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key).ToList();

        return new ExpertiseResult(Prompts.Expertise, skills, strongest, domains);
    }
}
