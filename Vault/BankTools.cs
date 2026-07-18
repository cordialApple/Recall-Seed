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
            (skills ?? []).Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name)).ToList(),
            Clean(tags),
            (metrics ?? []).Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Label)).ToList(),
            (entities ?? []).Select(NormalizeEntity).Where(e => e.Length > 0).Distinct().ToList(),
            Clean(gaps).ToList());

        if (error != null) return new WriteResult(null, Error: error);
        return new WriteResult(writtenId, path,
            "banked to the vault. keyword/structured tools see it now. confirm the draft by answering its gaps.");
    }

    [McpServerTool(Name = "update_experience")]
    [Description("Refine an existing vault note by id: patch only the fields you pass, leave the rest " +
                 "untouched. Per-beat text and confidence, plus skills/tags/metrics/entities/gaps as " +
                 "whole-list replacements (pass the new list; [] clears, omit keeps). Answer a gap by " +
                 "filling the beat it asks about and passing the trimmed gaps list. Grounding holds: " +
                 "never invent a fact/metric, keep metrics verbatim. Unknown id returns a clean error.")]
    public static WriteResult UpdateExperience(
        [Description("Id of the note to patch. From search_experiences, query_experiences, or get_experience.")] string id,
        [Description("New title. Omit to keep the current one.")] string? title = null,
        [Description("New situation beat text. Omit to keep, pass empty to clear.")] string? situation = null,
        [Description("New task beat text. Omit to keep, pass empty to clear.")] string? task = null,
        [Description("New action beat text. Omit to keep, pass empty to clear.")] string? action = null,
        [Description("New result beat text. Omit to keep, pass empty to clear.")] string? result = null,
        [Description("New situation confidence high|medium|low. Omit to keep.")] string? situationConfidence = null,
        [Description("New task confidence high|medium|low. Omit to keep.")] string? taskConfidence = null,
        [Description("New action confidence high|medium|low. Omit to keep.")] string? actionConfidence = null,
        [Description("New result confidence high|medium|low. Omit to keep.")] string? resultConfidence = null,
        [Description("New context: work, project, class, or other. Omit to keep.")] string? context = null,
        [Description("Replacement skills list, each { name, kind }. Omit to keep, [] to clear.")] VaultSkill[]? skills = null,
        [Description("Replacement tags list. Omit to keep, [] to clear.")] string[]? tags = null,
        [Description("Replacement metrics list, each { label, value?, unit? } — verbatim, never invented. Omit to keep, [] to clear.")] VaultMetric[]? metrics = null,
        [Description("Replacement entities list ([[wikilink]] or bare names). Omit to keep, [] to clear.")] string[]? entities = null,
        [Description("Replacement gap-questions list. Answer a gap by dropping it here. Omit to keep, [] to clear.")] string[]? gaps = null,
        [Description("New status: draft or confirmed. Omit to keep.")] string? status = null)
    {
        var (e, findError) = FindById(id);
        if (e is null) return findError!;

        if (title is not null && string.IsNullOrWhiteSpace(title))
            return new WriteResult(null, Error: "title must not be empty");
        if (status is not null && status.Trim() == "confirmed")
            return new WriteResult(null, Error: "use confirm_experience to confirm a note; update_experience cannot set status to confirmed");

        var newContext = context is null ? e.Context : context.Trim();
        if (!Contexts.Contains(newContext))
            return new WriteResult(null, Error: $"invalid context '{newContext}'; must be one of: {string.Join(", ", Contexts)}");
        var newStatus = status is null ? e.Status : status.Trim();
        if (!Statuses.Contains(newStatus))
            return new WriteResult(null, Error: $"invalid status '{newStatus}'; must be one of: {string.Join(", ", Statuses)}");

        string ConfFor(string? given, string beat) => given is null ? e.Confidence.GetValueOrDefault(beat, "low") : Conf(given);

        var beats = new[]
        {
            ("situation", situation ?? e.Situation, ConfFor(situationConfidence, "situation")),
            ("task", task ?? e.Task, ConfFor(taskConfidence, "task")),
            ("action", action ?? e.Action, ConfFor(actionConfidence, "action")),
            ("result", result ?? e.Result, ConfFor(resultConfidence, "result")),
        };
        if (beats.All(b => string.IsNullOrWhiteSpace(b.Item2)))
            return new WriteResult(null, Error: "at least one STAR beat (situation, task, action, result) is required");

        var (writtenId, path, error) = VaultStore.Write(
            e.Id, title is null ? e.Title : title.Trim(), newContext, newStatus, beats,
            skills is null ? e.Skills : skills.Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name)).ToList(),
            tags is null ? e.Tags : Clean(tags),
            metrics is null ? e.Metrics : metrics.Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Label)).ToList(),
            entities is null ? e.Entities : entities.Select(NormalizeEntity).Where(x => x.Length > 0).Distinct().ToList(),
            gaps is null ? e.Gaps : Clean(gaps));

        if (error != null) return new WriteResult(null, Error: error);
        return new WriteResult(writtenId, path, "patched. only the fields you passed changed; the rest is untouched.");
    }

    [McpServerTool(Name = "confirm_experience")]
    [Description("Flip a note's status from draft to confirmed by id — the person vouching the draft is " +
                 "true. Refuses if the action or result beat is empty or low-confidence, or open gaps " +
                 "remain, and reports exactly what blocks so you know what to fill with update_experience.")]
    public static WriteResult ConfirmExperience(
        [Description("Id of the note to confirm. A note already confirmed just re-confirms idempotently.")] string id)
    {
        var (e, findError) = FindById(id);
        if (e is null) return findError!;

        var blockers = new List<string>();
        if (e.Gaps.Count > 0)
            blockers.Add($"{e.Gaps.Count} open gap(s): {string.Join("; ", e.Gaps)}");
        foreach (var (beat, text) in new[] { ("action", e.Action), ("result", e.Result) })
        {
            if (string.IsNullOrWhiteSpace(text)) blockers.Add($"{beat} beat is empty");
            else if (e.Confidence.GetValueOrDefault(beat, "low") == "low") blockers.Add($"{beat} beat is low-confidence");
        }
        if (blockers.Count > 0)
            return new WriteResult(null, Error: "cannot confirm: " + string.Join(" | ", blockers));

        var beats = e.Beats.Select(b => (b.Beat, b.Text, e.Confidence.GetValueOrDefault(b.Beat, "low"))).ToArray();
        var (writtenId, path, error) = VaultStore.Write(
            e.Id, e.Title, e.Context, "confirmed", beats,
            e.Skills, e.Tags, e.Metrics, e.Entities, e.Gaps);

        if (error != null) return new WriteResult(null, Error: error);
        return new WriteResult(writtenId, path, "confirmed. status is now confirmed.");
    }

    static (Experience? Item, WriteResult? Error) FindById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return (null, new WriteResult(null, Error: "id must not be empty"));

        var (items, loadError) = VaultStore.LoadAll();
        if (loadError != null) return (null, new WriteResult(null, Error: loadError));

        var e = items.FirstOrDefault(x => x.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
        if (e is null) return (null, new WriteResult(null, Error: $"no experience with id '{id.Trim()}'"));
        return (e, null);
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
