using System.ComponentModel;
using ModelContextProtocol.Server;

namespace PersonalServer.Vault;

/// <summary>Voice rules plus fresh samples from the vault, for writing anything back in the person's own voice.</summary>
[McpServerToolType]
internal static class VoiceTools
{
    [McpServerTool(Name = "voice_guide")]
    [Description("Return the voice rules for writing anything under the person's name (STAR notes, " +
                 "bullets, posts, PRs) plus a few recent vault notes as fresh style samples — real " +
                 "writing beats a rulebook, so mirror the samples' phrasing over the frozen rules. Run " +
                 "user-facing text through this as a post-process layer.")]
    public static VoiceResult VoiceGuide(
        [Description("How many recent notes to pull as samples (1-8, default 4).")] int samples = 4)
    {
        var (items, error) = VaultStore.LoadAll();
        if (error != null) return new VoiceResult(Prompts.Voice, [], error);

        var recent = items
            .OrderByDescending(e => e.UpdatedUtc)
            .Take(Math.Clamp(samples, 1, 8))
            .Select(e => new VoiceSample(e.Id, Excerpt(e)))
            .ToList();

        return new VoiceResult(Prompts.Voice, recent);
    }

    static string Excerpt(Experience e)
    {
        var body = string.Join(" ", e.Beats.Select(b => b.Text)
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        return body.Length <= 320 ? body : body[..320] + "…";
    }
}
