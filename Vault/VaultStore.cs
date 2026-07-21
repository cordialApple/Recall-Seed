using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PersonalServer.Vault;

/// <summary>
/// Reads and writes the Obsidian-style experience vault as plain markdown — the single source of
/// truth. One experience = one file under experiences/; entity notes are the vault-root *.md files
/// that [[wikilinks]] resolve to, so backlinks are the knowledge graph for free.
/// </summary>
internal sealed partial class VaultStore : IExperienceStore
{
    public const string EnvVar = "EXPERIENCE_VAULT";

    static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(LowerCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static string ResolveVault()
    {
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "Design_Exp");
    }

    public static string ExperiencesDir() => Path.Combine(ResolveVault(), "experiences");

    public (IReadOnlyList<Experience> Items, string? Error) LoadAll()
    {
        var dir = ExperiencesDir();
        if (!Directory.Exists(dir))
            return ([], $"experience vault not found at '{dir}'; set {EnvVar} to your vault path");

        var items = new List<Experience>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
        {
            var exp = TryParse(file);
            if (exp != null) items.Add(exp);
        }
        return (items, null);
    }

    public IReadOnlyList<string> KnownEntities()
    {
        var vault = ResolveVault();
        if (!Directory.Exists(vault)) return [];
        return Directory.EnumerateFiles(vault, "*.md")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static Experience? TryParse(string file)
    {
        string text;
        try { text = File.ReadAllText(file); }
        catch { return null; }

        var (front, body) = Split(text);
        var fm = ParseFront(front);
        var id = fm.Id ?? Path.GetFileNameWithoutExtension(file);
        var sections = Sections(body);

        return new Experience(
            Id: id,
            Title: fm.Title ?? id,
            Context: fm.Context ?? "other",
            Status: fm.Status ?? "draft",
            Confidence: fm.Confidence ?? new Dictionary<string, string>(),
            Skills: (fm.Skills ?? []).Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .Select(s => new VaultSkill(s.Name!.Trim(), (s.Kind ?? "technical").Trim())).ToList(),
            Tags: (fm.Tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList(),
            Metrics: (fm.Metrics ?? []).Where(m => !string.IsNullOrWhiteSpace(m.Label))
                .Select(m => new VaultMetric(m.Label!.Trim(), m.Value, m.Unit?.Trim())).ToList(),
            Entities: (fm.Entities ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList(),
            Situation: sections.GetValueOrDefault("situation", ""),
            Task: sections.GetValueOrDefault("task", ""),
            Action: sections.GetValueOrDefault("action", ""),
            Result: sections.GetValueOrDefault("result", ""),
            Gaps: ParseGaps(sections.GetValueOrDefault("gaps", "")),
            Path: file,
            UpdatedUtc: SafeMtime(file));
    }

    static DateTime SafeMtime(string file)
    {
        try { return File.GetLastWriteTimeUtc(file); } catch { return DateTime.MinValue; }
    }

    static (string Front, string Body) Split(string text)
    {
        var m = FrontmatterRx().Match(text);
        return m.Success ? (m.Groups["front"].Value, text[m.Length..]) : ("", text);
    }

    static FrontmatterYaml ParseFront(string front)
    {
        if (string.IsNullOrWhiteSpace(front)) return new FrontmatterYaml();
        try { return Yaml.Deserialize<FrontmatterYaml>(front) ?? new FrontmatterYaml(); }
        catch { return new FrontmatterYaml(); }
    }

    static Dictionary<string, string> Sections(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var buf = new StringBuilder();
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var h = HeaderRx().Match(line);
            if (h.Success)
            {
                if (current != null) result[current] = buf.ToString().Trim();
                current = h.Groups["h"].Value.Trim().ToLowerInvariant();
                buf.Clear();
            }
            else if (current != null)
            {
                buf.Append(line).Append('\n');
            }
        }
        if (current != null) result[current] = buf.ToString().Trim();
        return result;
    }

    static IReadOnlyList<string> ParseGaps(string gapsSection)
    {
        var gaps = new List<string>();
        foreach (var line in gapsSection.Split('\n'))
        {
            var m = GapRx().Match(line);
            if (m.Success) gaps.Add(m.Groups["q"].Value.Trim());
        }
        return gaps;
    }

    public (string Id, string Path, string? Error) Write(
        string id, string title, string context, string status,
        (string Beat, string Text, string Confidence)[] beats,
        IReadOnlyList<VaultSkill> skills, IReadOnlyList<string> tags,
        IReadOnlyList<VaultMetric> metrics, IReadOnlyList<string> entities,
        IReadOnlyList<string> gaps)
    {
        var dir = ExperiencesDir();
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex) { return ("", "", ex.Message); }

        var path = Path.Combine(dir, id + ".md");
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("id: ").Append(id).Append('\n');
        sb.Append("title: ").Append(Q(title)).Append('\n');
        sb.Append("context: ").Append(context).Append('\n');
        sb.Append("status: ").Append(status).Append('\n');
        sb.Append("confidence:\n");
        foreach (var (beat, _, conf) in beats)
            sb.Append("  ").Append(beat).Append(": ").Append(conf).Append('\n');
        sb.Append("skills:\n");
        foreach (var s in skills)
            sb.Append("  - name: ").Append(Q(s.Name)).Append("\n    kind: ").Append(Q(s.Kind)).Append('\n');
        sb.Append("tags: [").Append(string.Join(", ", tags.Select(Q))).Append("]\n");
        sb.Append("metrics:");
        if (metrics.Count == 0) sb.Append(" []\n");
        else
        {
            sb.Append('\n');
            foreach (var m in metrics)
            {
                sb.Append("  - label: ").Append(Q(m.Label)).Append('\n');
                if (m.Value is { } v) sb.Append("    value: ").Append(v.ToString(CultureInfo.InvariantCulture)).Append('\n');
                if (!string.IsNullOrWhiteSpace(m.Unit)) sb.Append("    unit: ").Append(Q(m.Unit)).Append('\n');
            }
        }
        sb.Append("entities: [").Append(string.Join(", ", entities.Select(Q))).Append("]\n");
        sb.Append("---\n\n");

        foreach (var (beat, text, _) in beats)
            sb.Append("## ").Append(Cap(beat)).Append("\n").Append(text).Append("\n\n");

        if (gaps.Count > 0)
        {
            sb.Append("## Gaps\n");
            foreach (var g in gaps) sb.Append("- [ ] ").Append(g).Append('\n');
        }

        try { File.WriteAllText(path, sb.ToString()); }
        catch (Exception ex) { return ("", "", ex.Message); }
        return (id, path, null);
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static string Q(string? s)
        => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ") + "\"";

    [GeneratedRegex(@"\A---\r?\n(?<front>.*?)\r?\n---\r?\n?", RegexOptions.Singleline)]
    private static partial Regex FrontmatterRx();

    [GeneratedRegex(@"^\s*#{2,6}\s+(?<h>.+?)\s*$")]
    private static partial Regex HeaderRx();

    [GeneratedRegex(@"^\s*-\s*\[[ xX]\]\s*(?<q>.+?)\s*$")]
    private static partial Regex GapRx();

    sealed class FrontmatterYaml
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Context { get; set; }
        public string? Status { get; set; }
        public Dictionary<string, string>? Confidence { get; set; }
        public List<SkillYaml>? Skills { get; set; }
        public List<string>? Tags { get; set; }
        public List<MetricYaml>? Metrics { get; set; }
        public List<string>? Entities { get; set; }
    }

    sealed class SkillYaml
    {
        public string? Name { get; set; }
        public string? Kind { get; set; }
    }

    sealed class MetricYaml
    {
        public string? Label { get; set; }
        public double? Value { get; set; }
        public string? Unit { get; set; }
    }
}
