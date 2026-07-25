using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Recall_Seed.Vault;

/// <summary>Read primitives over the vault: free-text search, structured query, and fetch-one.</summary>
[McpServerToolType]
internal static class SearchTools
{
    [McpServerTool(Name = "search_experiences")]
    [Description("Free-text search across the experience vault — matches title, STAR beats, tags, " +
                 "skills, and linked entities — ranked by relevance with a snippet. Use to pull the " +
                 "specific experiences relevant to a question before grounding an answer in them.")]
    public static SearchResult SearchExperiences(
        [Description("The text to search for.")] string query,
        [Description("Maximum number of experiences to return (1-100, default 20).")] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchResult([], "query must not be empty");

        var (items, error) = ExperienceStore.Current.LoadAll();
        if (error != null) return new SearchResult([], error);

        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var hits = items
            .Select(e => (e, score: Score(e, words)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenByDescending(x => x.e.UpdatedUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(x => new ExperienceHit(x.e.Id, x.e.Title, x.e.Context, x.e.Status, Snippet(x.e, words)))
            .ToList();

        return new SearchResult(hits);
    }

    [McpServerTool(Name = "query_experiences")]
    [Description("Structured retrieval over the vault using named filters (never raw queries). Filter by " +
                 "any combination of skills, tags, context, and status; returns experience cards with " +
                 "their skills, tags, and metrics. Use search_experiences for free-text and neighbors for " +
                 "graph traversal.")]
    public static QueryResult QueryExperiences(
        [Description("Only experiences having at least one of these skill names.")] string[]? skills = null,
        [Description("Only experiences having at least one of these tag names.")] string[]? tags = null,
        [Description("Filter by context: work, project, class, or other.")] string? context = null,
        [Description("Filter by status: draft or confirmed.")] string? status = null,
        [Description("Maximum number of experiences to return (1-200, default 25).")] int limit = 25)
    {
        var (items, error) = ExperienceStore.Current.LoadAll();
        if (error != null) return new QueryResult([], error);

        var skillSet = Set(skills);
        var tagSet = Set(tags);

        var cards = items
            .Where(e => context == null || e.Context.Equals(context, StringComparison.OrdinalIgnoreCase))
            .Where(e => status == null || e.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .Where(e => skillSet.Count == 0 || e.Skills.Any(s => skillSet.Contains(s.Name)))
            .Where(e => tagSet.Count == 0 || e.Tags.Any(tagSet.Contains))
            .OrderByDescending(e => e.UpdatedUtc)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(e => new ExperienceCard(e.Id, e.Title, e.Context, e.Status,
                e.Skills.Select(s => s.Name).ToList(), e.Tags, e.Metrics))
            .ToList();

        return new QueryResult(cards);
    }

    [McpServerTool(Name = "get_experience")]
    [Description("Fetch one full experience note by its id — frontmatter, all STAR beats, metrics, " +
                 "entities, and open gaps. Use after search_experiences or query_experiences to read " +
                 "the complete grounded record.")]
    public static GetResult GetExperience(
        [Description("The experience id (slug), e.g. '2026-07-12-payments-migration'.")] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return new GetResult(null, "id must not be empty");

        var (items, error) = ExperienceStore.Current.LoadAll();
        if (error != null) return new GetResult(null, error);

        var found = items.FirstOrDefault(e => e.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
        return found == null ? new GetResult(null, $"no experience with id '{id}'") : new GetResult(found);
    }

    static HashSet<string> Set(string[]? values)
        => new((values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()),
            StringComparer.OrdinalIgnoreCase);

    static int CountHits(string lowered, string[] words) => words.Count(lowered.Contains);

    static int Score(Experience e, string[] words) => CountHits(Haystack(e), words);

    static string Haystack(Experience e)
        => (e.Title + " " + string.Join(" ", e.Tags) + " " +
            string.Join(" ", e.Skills.Select(s => s.Name)) + " " +
            string.Join(" ", e.Entities) + " " +
            string.Join(" ", e.Beats.Select(b => b.Text))).ToLowerInvariant();

    static string Snippet(Experience e, string[] words)
    {
        var best = e.Beats
            .Where(b => !string.IsNullOrWhiteSpace(b.Text))
            .Select(b => (b.Text, hits: CountHits(b.Text.ToLowerInvariant(), words)))
            .OrderByDescending(x => x.hits)
            .FirstOrDefault();

        var text = (best.hits > 0 ? best.Text : e.Title).Trim();
        return text.Length <= 200 ? text : text[..200] + "…";
    }
}
