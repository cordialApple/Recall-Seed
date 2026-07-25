using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Recall_Seed.Vault;

/// <summary>Defend-your-own-repo mock interview grounded in the person's real vault, with a hard citation guard.</summary>
[McpServerToolType]
internal static class InterviewTools
{
    [McpServerTool(Name = "defend_repo")]
    [Description("Start a defend-your-own-repo technical mock interview: returns the person's own " +
                 "experiences as cited corpus chunks (chunk_id + text), the interviewer prompt, and the " +
                 "ensureCited invariant. Ask one grounded question at a time; every non-terminal question " +
                 "MUST cite >=1 real chunk_id. Optionally scope the corpus to a topic. Validate cited ids " +
                 "with check_citation before asking.")]
    public static InterviewResult DefendRepo(
        [Description("Optional topic to scope the corpus to (matches title, tags, skills, and beat text). Omit for the whole vault.")] string? topic = null)
    {
        var (items, error) = ExperienceStore.Current.LoadAll();
        if (error != null) return new InterviewResult(Prompts.Interview, Prompts.CitationInvariant, [], error);
        if (items.Count == 0)
            return new InterviewResult(Prompts.Interview, Prompts.CitationInvariant, [], "the vault has no experiences yet; bank some first");

        var scoped = Scope(items, topic);
        var corpus = Chunks(scoped);
        if (corpus.Count == 0)
            return new InterviewResult(Prompts.Interview, Prompts.CitationInvariant, [], "no non-empty material to build a corpus from");

        return new InterviewResult(Prompts.Interview, Prompts.CitationInvariant, corpus);
    }

    [McpServerTool(Name = "check_citation")]
    [Description("Enforce the ensureCited invariant: given the chunk_ids you intend to cite in the next " +
                 "interview question, returns whether they all exist in the vault corpus and lists any " +
                 "unknown ones. A question that cites an unknown chunk_id is out of bounds — drop it.")]
    public static CitationCheck CheckCitation(
        [Description("The chunk_ids the next question will cite (e.g. '2026-07-12-payments-migration#action').")] string[] chunkIds)
    {
        var (items, error) = ExperienceStore.Current.LoadAll();
        if (error != null) return new CitationCheck(false, [], error);

        var valid = Chunks(items).Select(c => c.ChunkId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = (chunkIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id) && !valid.Contains(id.Trim()))
            .Distinct().ToList();

        return new CitationCheck(unknown.Count == 0, unknown);
    }

    static IReadOnlyList<Experience> Scope(IReadOnlyList<Experience> items, string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return items;
        var words = topic.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matched = items.Where(e =>
        {
            var hay = (e.Title + " " + string.Join(" ", e.Tags) + " " +
                       string.Join(" ", e.Skills.Select(s => s.Name)) + " " +
                       string.Join(" ", e.Beats.Select(b => b.Text))).ToLowerInvariant();
            return words.Any(w => hay.Contains(w));
        }).ToList();
        return matched.Count > 0 ? matched : items;
    }

    static IReadOnlyList<Chunk> Chunks(IReadOnlyList<Experience> items)
    {
        var chunks = new List<Chunk>();
        foreach (var e in items)
            foreach (var (beat, text) in e.Beats)
                if (!string.IsNullOrWhiteSpace(text))
                    chunks.Add(new Chunk($"{e.Id}#{beat}", text.Trim()));
        return chunks;
    }
}
