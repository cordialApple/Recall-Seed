using System.ComponentModel;
using ModelContextProtocol.Server;

namespace PersonalServer.Vault;

/// <summary>Turn one or more banked experiences into a grounded spoken behavioral interview answer.</summary>
[McpServerToolType]
internal static class StoryTools
{
    [McpServerTool(Name = "generate_story")]
    [Description("Assemble the named experiences plus the grounded behavioral-answer prompt and the " +
                 "voice rules, so you can write a spoken behavioral interview answer. Built ONLY from the " +
                 "provided experiences — metrics verbatim, provenance to their ids, nothing invented — " +
                 "then voiced to sound like the person. Pass experience ids from search_experiences or " +
                 "query_experiences.")]
    public static StoryResult GenerateStory(
        [Description("Ids of the experiences to ground the answer in. The story draws only on these.")] string[] experienceIds)
    {
        if (experienceIds is null || experienceIds.Length == 0)
            return new StoryResult(Prompts.BehavioralAnswer, Prompts.Voice, [], "experienceIds must not be empty");

        var (items, error) = VaultStore.LoadAll();
        if (error != null) return new StoryResult(Prompts.BehavioralAnswer, Prompts.Voice, [], error);

        var wanted = new HashSet<string>(experienceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var blocks = items
            .Where(e => wanted.Contains(e.Id))
            .Select(e => new ExperienceBlock(
                e.Id, e.Title, e.Context,
                e.Situation, e.Task, e.Action, e.Result,
                e.Metrics, e.Skills.Select(s => s.Name).ToList()))
            .ToList();

        if (blocks.Count == 0)
            return new StoryResult(Prompts.BehavioralAnswer, Prompts.Voice, [], "no experiences matched the provided ids");

        return new StoryResult(Prompts.BehavioralAnswer, Prompts.Voice, blocks);
    }
}
