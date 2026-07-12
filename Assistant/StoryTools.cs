using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace PersonalServer.Assistant;

/// <summary>Grounded STAR-story generation, proxied to a running STARfolio.</summary>
[McpServerToolType]
internal static class StoryTools
{
    [McpServerTool(Name = "generate_story")]
    [Description("Generate a grounded STAR story from specific experiences, proxied to STARfolio's " +
                 "generation service. Grounding is enforced STARfolio-side: the story is built only " +
                 "from the named experiences and carries provenance back to them — nothing is " +
                 "invented, and PersonalServer never edits or re-authors the returned text. Requires " +
                 "STARfolio to be running; if closed this returns error 'starfolio_not_running'.")]
    public static async Task<StoryResult> GenerateStory(
        [Description("Ids of the experiences to ground the story in (from query, search_nodes, or retrieve_semantic).")] string[] experienceIds,
        [Description("Optional genre/style, e.g. 'behavioral interview answer'.")] string? genre = null,
        [Description("Optional job description to tailor the story toward.")] string? jd = null,
        [Description("Optional target length, e.g. 'short', 'medium', 'long'.")] string? length = null)
    {
        if (experienceIds is null || experienceIds.Length == 0)
            return new StoryResult(null, [], "experience_ids must not be empty");

        var (body, error) = await Loopback.PostAsync("/generate",
            new { experience_ids = experienceIds, genre, jd, length });
        if (error != null)
            return new StoryResult(null, [], error);

        var root = body!.Value;
        var story = Loopback.Str(root, "story");

        var provenance = experienceIds;
        if (root.TryGetProperty("experience_ids", out var arr) && arr.ValueKind == JsonValueKind.Array)
            provenance = arr.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToArray();

        return new StoryResult(story, provenance);
    }
}
