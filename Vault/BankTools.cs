using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace PersonalServer.Vault;

/// <summary>Turn messy career evidence into a grounded STAR note, and commit it to the vault.</summary>
[McpServerToolType]
internal static partial class BankTools
{
    static readonly string[] Kinds = ["notes", "resume", "evidence"];
    static readonly string[] Contexts = ["work", "project", "class", "other"];
    static readonly string[] Statuses = ["draft", "confirmed"];

    [McpServerTool(Name = "bank_experience")]
    [Description("Start banking a career experience: returns the grounded STARfolio extraction prompt " +
                 "matching the input kind (notes/resume/evidence) plus the entity-extraction prompt, the " +
                 "STAR output schema, the vault write rules, and the vault's known entity names. You then " +
                 "apply the prompt to the input to produce a grounded STAR record, and commit it with " +
                 "write_experience. Grounding is the point: never invent facts/numbers/outcomes; leave " +
                 "absent beats thin with a gap question; keep metrics verbatim.")]
    public static BankResult BankExperience(
        [Description("The raw input to extract from — messy notes, resume text, or flattened code/repo/spreadsheet evidence.")] string input,
        [Description("Kind of input: 'notes', 'resume', or 'evidence'. Picks the matching grounded prompt.")] string kind = "notes")
    {
        if (string.IsNullOrWhiteSpace(input))
            return new BankResult(kind, "", "", "", "", [], "", "input must not be empty");
        if (!Kinds.Contains(kind))
            return new BankResult(kind, "", "", "", "", [], "", $"invalid kind '{kind}'; must be one of: {string.Join(", ", Kinds)}");

        var instruction = kind switch
        {
            "resume" => Prompts.Resume,
            "evidence" => Prompts.Evidence,
            _ => Prompts.Extract
        };

        return new BankResult(kind, instruction, Prompts.Entity, Prompts.OutputSchema,
            Prompts.WriteRules, VaultStore.KnownEntities(), input);
    }

    [McpServerTool(Name = "write_experience")]
    [Description("Commit a grounded STAR note to the experience vault as one markdown file under " +
                 "experiences/. Shape the record with bank_experience first — this is the commit step. " +
                 "Writes frontmatter (id/title/context/status/confidence/skills/tags/metrics/entities) " +
                 "and the STAR beats, with gaps as todo checkboxes. Metrics are written verbatim; do not " +
                 "invent any. Entities should be [[wikilink]] targets (bare names accepted).")]
    public static WriteResult WriteExperience(
        [Description("Stable unique slug id, e.g. '2026-07-12-payments-migration'. It is what resume bullets cite. Derived from the title if omitted.")] string? id,
        [Description("Short title for the experience.")] string title,
        [Description("Situation beat text (context/background). Leave empty if the source doesn't support it.")] string? situation = null,
        [Description("Task beat text (what needed doing).")] string? task = null,
        [Description("Action beat text (what you did).")] string? action = null,
        [Description("Result beat text (outcome, ideally quantified).")] string? result = null,
        [Description("Confidence for the situation beat: high|medium|low.")] string situationConfidence = "low",
        [Description("Confidence for the task beat: high|medium|low.")] string taskConfidence = "low",
        [Description("Confidence for the action beat: high|medium|low.")] string actionConfidence = "low",
        [Description("Confidence for the result beat: high|medium|low.")] string resultConfidence = "low",
        [Description("Context: work, project, class, or other.")] string context = "work",
        [Description("Skills to link, each { name, kind: technical|soft|domain }.")] VaultSkill[]? skills = null,
        [Description("Short topical tags.")] string[]? tags = null,
        [Description("Metrics, each { label, value?, unit? } — verbatim from the source, never invented.")] VaultMetric[]? metrics = null,
        [Description("Named entities as [[wikilink]] targets or bare names (people/teams/projects/orgs/tools).")] string[]? entities = null,
        [Description("Gap questions for thin/absent beats — the person answers these to confirm the draft.")] string[]? gaps = null,
        [Description("Status: draft or confirmed (default draft).")] string status = "draft")
    {
        if (string.IsNullOrWhiteSpace(title))
            return new WriteResult(null, Error: "title must not be empty");
        if (string.IsNullOrWhiteSpace(situation) && string.IsNullOrWhiteSpace(task)
            && string.IsNullOrWhiteSpace(action) && string.IsNullOrWhiteSpace(result))
            return new WriteResult(null, Error: "at least one STAR beat (situation, task, action, result) is required");
        if (!Contexts.Contains(context))
            return new WriteResult(null, Error: $"invalid context '{context}'; must be one of: {string.Join(", ", Contexts)}");
        if (!Statuses.Contains(status))
            return new WriteResult(null, Error: $"invalid status '{status}'; must be one of: {string.Join(", ", Statuses)}");

        var slug = string.IsNullOrWhiteSpace(id) ? Slug(title) : Slug(id!);
        if (slug.Length == 0)
            return new WriteResult(null, Error: "could not derive a valid id slug; pass an explicit id");

        var beats = new[]
        {
            ("situation", situation ?? "", Conf(situationConfidence)),
            ("task", task ?? "", Conf(taskConfidence)),
            ("action", action ?? "", Conf(actionConfidence)),
            ("result", result ?? "", Conf(resultConfidence)),
        };

        var (writtenId, path, error) = VaultStore.Write(
            slug, title, context, status, beats,
            (skills ?? []).Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList(),
            Clean(tags),
            (metrics ?? []).Where(m => !string.IsNullOrWhiteSpace(m.Label)).ToList(),
            (entities ?? []).Select(NormalizeEntity).Where(e => e.Length > 0).Distinct().ToList(),
            Clean(gaps).ToList());

        if (error != null) return new WriteResult(null, Error: error);
        return new WriteResult(writtenId, path,
            "banked to the vault. keyword/structured tools see it now. confirm the draft by answering its gaps.");
    }

    static string Conf(string c) => c is "high" or "medium" or "low" ? c : "low";

    static List<string> Clean(string[]? values)
        => (values ?? []).Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).Distinct().ToList();

    static string NormalizeEntity(string raw)
    {
        var t = (raw ?? "").Trim();
        if (t.StartsWith("[[") && t.EndsWith("]]")) t = t[2..^2].Trim();
        return t.Length == 0 ? "" : $"[[{t}]]";
    }

    static string Slug(string s)
    {
        var lower = (s ?? "").Trim().ToLowerInvariant();
        return SlugRx().Replace(lower, "-").Trim('-');
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugRx();
}
