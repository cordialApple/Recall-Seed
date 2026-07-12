namespace PersonalServer.Assistant;

public record SemanticHit(string ExperienceId, string Title, double Score, string? Snippet);

public record RetrieveResult(IReadOnlyList<SemanticHit> Results, string? Error = null);

public record StoryResult(string? Story, IReadOnlyList<string> ExperienceIds, string? Error = null);
