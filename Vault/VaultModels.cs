using System.ComponentModel;

namespace PersonalServer.Vault;

public record VaultSkill(string Name, string Kind);

public record VaultMetric(
    [property: Description("Metric label, verbatim from the note.")] string Label,
    [property: Description("Numeric value, if the note states one.")] double? Value = null,
    [property: Description("Unit, if any.")] string? Unit = null);

public record Experience(
    string Id,
    string Title,
    string Context,
    string Status,
    IReadOnlyDictionary<string, string> Confidence,
    IReadOnlyList<VaultSkill> Skills,
    IReadOnlyList<string> Tags,
    IReadOnlyList<VaultMetric> Metrics,
    IReadOnlyList<string> Entities,
    string Situation,
    string Task,
    string Action,
    string Result,
    IReadOnlyList<string> Gaps,
    string Path,
    DateTime UpdatedUtc)
{
    public IEnumerable<(string Beat, string Text)> Beats =>
    [
        ("situation", Situation),
        ("task", Task),
        ("action", Action),
        ("result", Result),
    ];
}

public record NameCount(string Name, int Count);

public record BankResult(
    string Kind,
    string Instruction,
    string EntityInstruction,
    string OutputSchema,
    string WriteRules,
    IReadOnlyList<string> KnownEntities,
    string Input,
    string? Error = null);

public record WriteResult(string? Id, string? Path = null, string? Note = null, string? Error = null);

public record ExperienceBlock(
    string Id,
    string Title,
    string Context,
    string Situation,
    string Task,
    string Action,
    string Result,
    IReadOnlyList<VaultMetric> Metrics,
    IReadOnlyList<string> Skills);

public record TailorResult(
    string Instruction,
    string Jd,
    IReadOnlyList<ExperienceBlock> Experiences,
    string? Error = null);

public record TendencyStats(
    int ExperienceCount,
    int Confirmed,
    int Draft,
    int WithMetrics,
    int WithoutMetrics,
    int OpenGaps,
    IReadOnlyList<NameCount> Skills,
    IReadOnlyList<NameCount> SkillKinds,
    IReadOnlyList<NameCount> Tags,
    IReadOnlyList<NameCount> Contexts,
    IReadOnlyList<NameCount> Entities);

public record TendencyResult(string Instruction, TendencyStats Stats, string? Error = null);

public record SkillEvidence(string Name, string Kind, int Count, IReadOnlyList<string> ExperienceIds);

public record ExperienceRef(string Id, string Title, IReadOnlyList<VaultMetric> Metrics);

public record ExpertiseResult(
    string Instruction,
    IReadOnlyList<SkillEvidence> Skills,
    IReadOnlyList<ExperienceRef> StrongestResults,
    IReadOnlyList<string> Domains,
    string? Error = null);

public record Chunk(string ChunkId, string Text);

public record InterviewResult(
    string Instruction,
    string Invariant,
    IReadOnlyList<Chunk> Corpus,
    string? Error = null);

public record CitationCheck(bool Ok, IReadOnlyList<string> Unknown, string? Error = null);

public record VoiceSample(string Id, string Excerpt);

public record VoiceResult(string Rules, IReadOnlyList<VoiceSample> RecentSamples, string? Error = null);
