using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Recall_Seed.Vault;

/// <summary>Graph traversal over the vault — connections emerge from shared [[wikilinks]] and skills.</summary>
[McpServerToolType]
internal static class GraphTools
{
    [McpServerTool(Name = "neighbors")]
    [Description("Return the neighborhood of one experience: the entities it links to via [[wikilinks]], " +
                 "plus other experiences connected to it by a shared entity or a shared skill (each " +
                 "labeled with the via-name). The graph is emergent from the vault's own backlinks — no " +
                 "edges table. Pass an experience id from search_experiences or query_experiences.")]
    public static NeighborsResult Neighbors(
        [Description("The id of the experience to find neighbors for.")] string experienceId)
    {
        if (string.IsNullOrWhiteSpace(experienceId))
            return new NeighborsResult([], [], "experienceId must not be empty");

        var (items, error) = ExperienceStore.Current.LoadAll();
        if (error != null) return new NeighborsResult([], [], error);

        var target = items.FirstOrDefault(e => e.Id.Equals(experienceId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (target == null) return new NeighborsResult([], [], $"no experience with id '{experienceId}'");

        var targetEntities = new HashSet<string>(target.Entities, StringComparer.OrdinalIgnoreCase);
        var targetSkills = new HashSet<string>(target.Skills.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

        var connections = items
            .Where(e => !e.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase))
            .Select(e => new NeighborConnection(
                e.Id, e.Title,
                e.Entities.Where(targetEntities.Contains).ToList(),
                e.Skills.Select(s => s.Name).Where(targetSkills.Contains).ToList()))
            .Where(c => c.ViaEntities.Count > 0 || c.ViaSkills.Count > 0)
            .OrderByDescending(c => c.ViaEntities.Count + c.ViaSkills.Count)
            .ToList();

        return new NeighborsResult(target.Entities, connections);
    }
}
