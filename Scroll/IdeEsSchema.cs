using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Recall_Seed.Scroll;

/// <summary>
/// C# mirror of Scroll's ide-es spawn schema (docs/contracts/ide-es.v1.json, src/es/schema.ts). This is
/// PersonalServer depending on Scroll's versioned contract: the fields, the schemaVersion/kind pins, and
/// the validator all track validateIdeEsSchema exactly — no looser, no stricter — so we never hand the
/// user a spawn URL that create_ide_es would reject, and never change the contract unilaterally.
/// </summary>
internal sealed record IdeEsSchema(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("lifecycleOwner")] string LifecycleOwner,
    [property: JsonPropertyName("programmatic")] string Programmatic,
    [property: JsonPropertyName("goalCondition")] string GoalCondition,
    [property: JsonPropertyName("problem")] string Problem,
    [property: JsonPropertyName("entry")] IdeEsEntry Entry,
    [property: JsonPropertyName("tests")] IReadOnlyList<IdeEsTest> Tests,
    [property: JsonPropertyName("tleBudgetMs")] double TleBudgetMs,
    [property: JsonPropertyName("complexityBudget")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ComplexityBudget = null,
    [property: JsonPropertyName("hints")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<IdeEsHint>? Hints = null,
    [property: JsonPropertyName("resultCallbackUrl")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResultCallbackUrl = null)
{
    public const int Version = 1;
    public const string KindIdeEs = "ide-es";
    public const string EntryLanguage = "javascript";

    // Scroll's decodeSchema returns null past this base64url length (src/es/programmatic.ts).
    public const int MaxParamLen = 65536;

    static readonly string[] Owners = ["human", "consumer"];
    static readonly string[] Presets = ["on", "off"];
    static readonly Regex JsIdentifier = new(@"^[A-Za-z_$][A-Za-z0-9_$]*$", RegexOptions.Compiled);

    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Mirror of validateIdeEsSchema. Returns every failure so the tool reports them at once.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (SchemaVersion != Version) errors.Add($"schemaVersion must be {Version}");
        if (Kind != KindIdeEs) errors.Add($"kind must be '{KindIdeEs}'");
        if (string.IsNullOrEmpty(Title)) errors.Add("title must be a non-empty string");
        if (Array.IndexOf(Owners, LifecycleOwner) < 0) errors.Add("lifecycleOwner must be 'human' or 'consumer'");
        if (Array.IndexOf(Presets, Programmatic) < 0) errors.Add("programmatic must be 'on' or 'off'");
        if (string.IsNullOrEmpty(GoalCondition)) errors.Add("goalCondition must be a non-empty string");
        if (string.IsNullOrEmpty(Problem)) errors.Add("problem must be a non-empty string");

        if (Entry is null || Entry.Language != EntryLanguage || string.IsNullOrEmpty(Entry.FunctionName))
            errors.Add("entry must be { language: 'javascript', functionName: string }");
        else if (!JsIdentifier.IsMatch(Entry.FunctionName))
            errors.Add("entry.functionName must be a valid JS identifier");

        if (!(TleBudgetMs > 0)) errors.Add("tleBudgetMs must be a positive number");

        if (Tests is null || Tests.Count == 0) errors.Add("tests must be a non-empty array");
        else if (!Tests.Any(t => t.Hidden)) errors.Add("at least one test must be hidden (the graded oracle)");

        if (ResultCallbackUrl is not null && !ScrollSpawnConfig.IsHttpUrl(ResultCallbackUrl))
            errors.Add("resultCallbackUrl must be an http(s) URL when present");

        return errors;
    }

    /// <summary>base64url(JSON) matching src/es/programmatic.ts toBase64Url exactly (UTF-8, -/_ , no padding).</summary>
    public string EncodeParam()
    {
        var json = JsonSerializer.Serialize(this, Json);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

internal sealed record IdeEsEntry(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("functionName")] string FunctionName);

internal sealed record IdeEsTest(
    [property: JsonPropertyName("input")] string Input,
    [property: JsonPropertyName("expectedOutput")] string ExpectedOutput,
    [property: JsonPropertyName("hidden")] bool Hidden,
    [property: JsonPropertyName("name")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null);

internal sealed record IdeEsHint(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("afterFailedAttempts")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? AfterFailedAttempts = null);
