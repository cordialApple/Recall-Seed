using PersonalServer.Vault;

namespace PersonalServer.Tests;

public class VaultToolTests : IDisposable
{
    readonly string _vault;

    public VaultToolTests()
    {
        _vault = Path.Combine(Path.GetTempPath(), "psrv-vault-" + Guid.NewGuid().ToString("N"));
        var exp = Path.Combine(_vault, "experiences");
        Directory.CreateDirectory(exp);

        File.WriteAllText(Path.Combine(_vault, "Payments.md"), "# Payments\n");
        File.WriteAllText(Path.Combine(_vault, "React.md"), "# React\n");

        File.WriteAllText(Path.Combine(exp, "a-mig.md"), """
            ---
            id: a-mig
            title: Led database migration
            context: work
            status: confirmed
            confidence:
              situation: high
              task: high
              action: high
              result: high
            skills:
              - name: leadership
                kind: soft
              - name: sql
                kind: technical
            tags: [backend]
            metrics:
              - label: downtime
                value: 0
                unit: min
            entities: ["[[Payments]]"]
            ---

            ## Situation
            payments db was on an unsupported version.

            ## Task
            plan and run the cutover with zero downtime.

            ## Action
            coordinated the migration and ran the cutover.

            ## Result
            zero downtime during the switch.
            """);

        File.WriteAllText(Path.Combine(exp, "b-dash.md"), """
            ---
            id: b-dash
            title: Built React dashboard
            context: project
            status: draft
            confidence:
              situation: low
              action: high
            skills:
              - name: react
                kind: technical
            tags: [frontend]
            metrics: []
            entities: ["[[React]]"]
            ---

            ## Situation

            ## Action
            shipped the dashboard UI in React.

            ## Gaps
            - [ ] what problem did the dashboard solve?
            - [ ] any adoption metric?
            """);

        Environment.SetEnvironmentVariable(VaultStore.EnvVar, _vault);
    }

    [Fact]
    public void BankExperience_returns_matching_prompt_and_known_entities()
    {
        var notes = BankTools.BankExperience("did a thing", "notes");
        Assert.Null(notes.Error);
        Assert.Contains("messy notes", notes.Instruction);
        Assert.Contains("Payments", notes.KnownEntities);
        Assert.Contains("React", notes.KnownEntities);

        Assert.Contains("resume", BankTools.BankExperience("x", "resume").Instruction);
        Assert.Contains("evidence", BankTools.BankExperience("x", "evidence").Instruction);
    }

    [Fact]
    public void BankExperience_validates_input_and_kind()
    {
        Assert.Equal("input must not be empty", BankTools.BankExperience("  ", "notes").Error);
        Assert.Contains("invalid kind", BankTools.BankExperience("x", "bogus").Error);
    }

    [Fact]
    public void WriteExperience_persists_a_grounded_note_visible_to_other_tools()
    {
        var before = TendencyTools.FindTendencies().Stats.ExperienceCount;

        var res = BankTools.WriteExperience(
            id: "2026-07-12-cache-fix", title: "Cut API latency",
            situation: null, task: null, action: "added a read-through cache", result: "p95 dropped",
            actionConfidence: "high", context: "work",
            skills: [new VaultSkill("caching", "technical")],
            tags: ["performance"],
            metrics: [new VaultMetric("p95 latency", 40, "ms")],
            entities: ["Redis", "[[Payments]]"],
            gaps: ["what was the baseline latency?"]);

        Assert.Null(res.Error);
        Assert.Equal("2026-07-12-cache-fix", res.Id);
        Assert.True(File.Exists(res.Path));

        var file = File.ReadAllText(res.Path!);
        Assert.Contains("label: p95 latency", file);
        Assert.Contains("value: 40", file);
        Assert.Contains("[[Redis]]", file);

        Assert.Equal(before + 1, TendencyTools.FindTendencies().Stats.ExperienceCount);
    }

    [Fact]
    public void WriteExperience_validates_title_and_beats()
    {
        Assert.Equal("title must not be empty", BankTools.WriteExperience(null, "  ").Error);
        Assert.Contains("at least one STAR beat", BankTools.WriteExperience("id", "T").Error);
        Assert.Contains("invalid context", BankTools.WriteExperience("id", "T", action: "did", context: "nope").Error);
    }

    [Fact]
    public void TailorBullets_returns_blocks_with_verbatim_metrics()
    {
        var res = ResumeTools.TailorBullets("backend engineer, databases");
        Assert.Null(res.Error);
        Assert.Contains("experience_id", res.Instruction);

        var mig = Assert.Single(res.Experiences, b => b.Id == "a-mig");
        var metric = Assert.Single(mig.Metrics);
        Assert.Equal("downtime", metric.Label);
        Assert.Equal(0, metric.Value);
        Assert.Equal("min", metric.Unit);

        Assert.Equal("jd must not be empty", ResumeTools.TailorBullets(" ").Error);
    }

    [Fact]
    public void FindTendencies_aggregates_real_counts()
    {
        var s = TendencyTools.FindTendencies().Stats;
        Assert.Equal(2, s.ExperienceCount);
        Assert.Equal(1, s.Confirmed);
        Assert.Equal(1, s.Draft);
        Assert.Equal(1, s.WithMetrics);
        Assert.Equal(1, s.WithoutMetrics);
        Assert.Equal(2, s.OpenGaps);
        Assert.Contains(s.Skills, c => c.Name == "leadership" && c.Count == 1);
        Assert.Contains(s.Contexts, c => c.Name == "work" && c.Count == 1);
        Assert.Contains(s.Contexts, c => c.Name == "project" && c.Count == 1);
    }

    [Fact]
    public void ExpertiseProfile_evidences_skills_and_strongest_results()
    {
        var res = ExpertiseTools.ExpertiseProfile();
        Assert.Null(res.Error);

        var sql = Assert.Single(res.Skills, s => s.Name == "sql");
        Assert.Equal("technical", sql.Kind);
        Assert.Contains("a-mig", sql.ExperienceIds);

        var strong = Assert.Single(res.StrongestResults);
        Assert.Equal("a-mig", strong.Id);
        Assert.Contains("backend", res.Domains);
    }

    [Fact]
    public void DefendRepo_builds_cited_chunks_and_scopes_by_topic()
    {
        var all = InterviewTools.DefendRepo();
        Assert.Null(all.Error);
        Assert.Contains(all.Corpus, c => c.ChunkId == "a-mig#action");
        Assert.DoesNotContain(all.Corpus, c => c.ChunkId == "b-dash#situation");
        Assert.Contains("ensureCited", all.Invariant);

        var scoped = InterviewTools.DefendRepo("react dashboard");
        Assert.All(scoped.Corpus, c => Assert.StartsWith("b-dash", c.ChunkId));
    }

    [Fact]
    public void CheckCitation_enforces_real_chunk_ids()
    {
        var ok = InterviewTools.CheckCitation(["a-mig#action"]);
        Assert.True(ok.Ok);
        Assert.Empty(ok.Unknown);

        var bad = InterviewTools.CheckCitation(["a-mig#action", "a-mig#invented"]);
        Assert.False(bad.Ok);
        Assert.Contains("a-mig#invented", bad.Unknown);
    }

    [Fact]
    public void VoiceGuide_returns_rules_and_recent_samples()
    {
        var res = VoiceTools.VoiceGuide(2);
        Assert.Null(res.Error);
        Assert.Contains("lowercase", res.Rules);
        Assert.NotEmpty(res.RecentSamples);
    }

    [Fact]
    public void SearchExperiences_ranks_matches_with_snippet()
    {
        var res = SearchTools.SearchExperiences("cutover downtime");
        Assert.Null(res.Error);
        var top = res.Hits.First();
        Assert.Equal("a-mig", top.Id);
        Assert.False(string.IsNullOrWhiteSpace(top.Snippet));

        Assert.Empty(SearchTools.SearchExperiences("nonexistentterm").Hits);
        Assert.Equal("query must not be empty", SearchTools.SearchExperiences(" ").Error);
    }

    [Fact]
    public void QueryExperiences_filters_by_named_facets()
    {
        Assert.Equal("a-mig", Assert.Single(SearchTools.QueryExperiences(skills: ["sql"]).Experiences).Id);
        Assert.Equal("b-dash", Assert.Single(SearchTools.QueryExperiences(context: "project").Experiences).Id);
        Assert.Equal("a-mig", Assert.Single(SearchTools.QueryExperiences(status: "confirmed").Experiences).Id);
        Assert.Equal(2, SearchTools.QueryExperiences().Experiences.Count);
    }

    [Fact]
    public void GetExperience_returns_full_note_or_error()
    {
        var res = SearchTools.GetExperience("a-mig");
        Assert.Null(res.Error);
        Assert.Equal("Led database migration", res.Experience!.Title);
        Assert.Contains("cutover", res.Experience.Action);

        Assert.Contains("no experience with id", SearchTools.GetExperience("ghost").Error);
    }

    [Fact]
    public void Neighbors_connects_via_shared_entity_and_skill()
    {
        BankTools.WriteExperience(
            id: "c-pay", title: "More payments work", action: "tuned payments queries",
            actionConfidence: "high", context: "work",
            skills: [new VaultSkill("sql", "technical")], tags: null, metrics: null,
            entities: ["Payments"], gaps: null);

        var res = GraphTools.Neighbors("a-mig");
        Assert.Null(res.Error);
        Assert.Contains("[[Payments]]", res.Entities);

        var conn = Assert.Single(res.Connections, c => c.ExperienceId == "c-pay");
        Assert.Contains("[[Payments]]", conn.ViaEntities);
        Assert.Contains("sql", conn.ViaSkills);

        Assert.Contains("no experience with id", GraphTools.Neighbors("ghost").Error);
    }

    [Fact]
    public void GenerateStory_grounds_answer_in_named_experiences_with_voice()
    {
        var res = StoryTools.GenerateStory(["a-mig"]);
        Assert.Null(res.Error);
        Assert.Contains("behavioral", res.Instruction);
        Assert.Contains("lowercase", res.VoiceRules);

        var block = Assert.Single(res.Experiences);
        Assert.Equal("a-mig", block.Id);
        Assert.Equal("downtime", Assert.Single(block.Metrics).Label);

        Assert.Equal("experienceIds must not be empty", StoryTools.GenerateStory([]).Error);
        Assert.Contains("no experiences matched", StoryTools.GenerateStory(["ghost"]).Error);
    }

    [Fact]
    public void GenerateStory_accepts_length_tone_kind_knobs_and_defaults()
    {
        var def = StoryTools.GenerateStory(["a-mig"]);
        Assert.Null(def.Error);
        Assert.Equal("genre", def.Kind);
        Assert.Contains("90-second", def.Directive);
        Assert.Contains("professional", def.Directive);

        var jd = StoryTools.GenerateStory(["a-mig"], length: "short", tone: "confident", kind: "jd", target: "backend role, databases");
        Assert.Null(jd.Error);
        Assert.Equal("jd", jd.Kind);
        Assert.Equal("backend role, databases", jd.Target);
        Assert.Contains("30-second", jd.Directive);
        Assert.Contains("confident", jd.Directive);

        Assert.Contains("length must be", StoryTools.GenerateStory(["a-mig"], length: "epic").Error);
        Assert.Contains("tone must be", StoryTools.GenerateStory(["a-mig"], tone: "sassy").Error);
        Assert.Contains("kind must be", StoryTools.GenerateStory(["a-mig"], kind: "bogus").Error);
        Assert.Contains("needs a target", StoryTools.GenerateStory(["a-mig"], kind: "jd").Error);
    }

    [Fact]
    public void Tools_report_missing_vault()
    {
        Environment.SetEnvironmentVariable(VaultStore.EnvVar, Path.Combine(_vault, "does-not-exist"));
        Assert.Contains("not found", TendencyTools.FindTendencies().Error);
        Assert.Contains("not found", ResumeTools.TailorBullets("x").Error);
        Assert.Contains("not found", SearchTools.SearchExperiences("x").Error);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(VaultStore.EnvVar, null);
        try { Directory.Delete(_vault, true); } catch { }
    }
}
