using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Recall_Seed.Scroll;

namespace Recall_Seed.Tests;

static class B64
{
    // Inverse of Scroll's src/es/programmatic.ts fromBase64Url — proves our encoder is decodable there.
    public static string Decode(string param)
    {
        var s = param.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}

public class ScrollSpawnConfigTests
{
    static string? Base(string? raw) =>
        ScrollSpawnConfig.ResolveBaseUrl(new Dictionary<string, string?> { [ScrollSpawnConfig.BaseUrlEnv] = raw });

    [Fact]
    public void Defaults_when_unset_and_normalizes_trailing_slash()
    {
        Assert.Equal(ScrollSpawnConfig.DefaultBaseUrl, Base(null));
        Assert.Equal(ScrollSpawnConfig.DefaultBaseUrl, Base(""));
        Assert.Equal(ScrollSpawnConfig.DefaultBaseUrl, Base("   "));
        Assert.Equal("https://scroll.example", Base("https://scroll.example/"));
        Assert.Equal("http://host:8080", Base(" http://host:8080 "));
    }

    [Fact]
    public void Rejects_non_http_or_fragment_bearing_base()
    {
        Assert.Null(Base("scroll.example"));
        Assert.Null(Base("ftp://scroll.example"));
        Assert.Null(Base("javascript:alert(1)"));
        Assert.Null(Base("http://host/#already"));
    }

    [Fact]
    public void IsHttpUrl_matches_http_and_https_only()
    {
        Assert.True(ScrollSpawnConfig.IsHttpUrl("http://x"));
        Assert.True(ScrollSpawnConfig.IsHttpUrl("https://x"));
        Assert.False(ScrollSpawnConfig.IsHttpUrl("data:x"));
        Assert.False(ScrollSpawnConfig.IsHttpUrl(null));
        Assert.False(ScrollSpawnConfig.IsHttpUrl("HTTP://x")); // case-sensitive, mirrors Scroll's isHttpUrl
    }

    [Fact]
    public void CallbackUrl_is_loopback_with_correlation_and_spawn_url_carries_the_hash_route()
    {
        Assert.Equal("http://127.0.0.1:5599/?c=abc123", ScrollSpawnConfig.CallbackUrl(5599, "abc123"));
        Assert.Equal("http://host#/es/new?s=PAYLOAD", ScrollSpawnConfig.SpawnUrl("http://host", "PAYLOAD"));
    }
}

public class IdeEsSchemaTests
{
    static IdeEsSchema Valid(string? callback = null) => new(
        SchemaVersion: IdeEsSchema.Version, Kind: IdeEsSchema.KindIdeEs, Title: "Two Sum",
        LifecycleOwner: "consumer", Programmatic: "on", GoalCondition: "return indices",
        Problem: "find two numbers", Entry: new IdeEsEntry("javascript", "solve"),
        Tests: [new IdeEsTest("[1,2]", "3", false, "sample"), new IdeEsTest("[big]", "x", true)],
        TleBudgetMs: 1000, ResultCallbackUrl: callback);

    [Fact]
    public void Valid_schema_has_no_errors()
    {
        Assert.Empty(Valid().Validate());
        Assert.Empty(Valid("http://127.0.0.1:5599/?c=x").Validate());
    }

    [Theory]
    [InlineData("title")]
    [InlineData("goalCondition")]
    [InlineData("problem")]
    [InlineData("functionName")]
    [InlineData("language")]
    [InlineData("tle")]
    [InlineData("nohidden")]
    [InlineData("notests")]
    [InlineData("owner")]
    [InlineData("callback")]
    public void Validator_mirrors_scroll_field_rules(string broken)
    {
        var v = Valid();
        var bad = broken switch
        {
            "title" => v with { Title = "" },
            "goalCondition" => v with { GoalCondition = "" },
            "problem" => v with { Problem = "" },
            "functionName" => v with { Entry = new IdeEsEntry("javascript", "2bad") },
            "language" => v with { Entry = new IdeEsEntry("python", "solve") },
            "tle" => v with { TleBudgetMs = 0 },
            "nohidden" => v with { Tests = [new IdeEsTest("a", "b", false)] },
            "notests" => v with { Tests = [] },
            "owner" => v with { LifecycleOwner = "robot" },
            "callback" => v with { ResultCallbackUrl = "ftp://x" },
            _ => v,
        };
        Assert.NotEmpty(bad.Validate());
    }

    [Fact]
    public void EncodeParam_is_base64url_and_round_trips_to_the_expected_json()
    {
        var param = Valid("http://127.0.0.1:5599/?c=abc").EncodeParam();
        Assert.Matches(new Regex("^[A-Za-z0-9_-]+$"), param);

        using var doc = JsonDocument.Parse(B64.Decode(param));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("ide-es", root.GetProperty("kind").GetString());
        Assert.Equal("on", root.GetProperty("programmatic").GetString());
        Assert.Equal("javascript", root.GetProperty("entry").GetProperty("language").GetString());
        Assert.Equal("solve", root.GetProperty("entry").GetProperty("functionName").GetString());
        Assert.Equal(2, root.GetProperty("tests").GetArrayLength());
        Assert.Equal(1000, root.GetProperty("tleBudgetMs").GetDouble());
        Assert.Equal("http://127.0.0.1:5599/?c=abc", root.GetProperty("resultCallbackUrl").GetString());
    }

    [Fact]
    public void Optional_fields_are_omitted_when_absent()
    {
        using var doc = JsonDocument.Parse(B64.Decode(Valid().EncodeParam()));
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("resultCallbackUrl", out _));
        Assert.False(root.TryGetProperty("complexityBudget", out _));
        Assert.False(root.TryGetProperty("hints", out _));
        // the named test keeps its name; the un-named one drops the optional field
        Assert.Equal("sample", root.GetProperty("tests")[0].GetProperty("name").GetString());
        Assert.False(root.GetProperty("tests")[1].TryGetProperty("name", out _));
    }
}

public class ScrollSeedToolTests
{
    static readonly SeedTestCase[] Tests = [new("[1,2]", "3", false, "sample"), new("[big]", "x", true)];

    static SeedResult Build(string? baseUrl, int? port) => ScrollSeedTools.Build(
        baseUrl, port, "corr-fixed", "Two Sum", "find two", "return indices", "solve", Tests, 1000);

    [Fact]
    public void Wires_callback_and_correlation_when_receiver_is_on()
    {
        var r = Build("http://localhost:5173", 5599);
        Assert.Null(r.Errors);
        Assert.True(r.CallbackWired);
        Assert.Equal("corr-fixed", r.SpawnId);
        Assert.StartsWith("http://localhost:5173#/es/new?s=", r.SpawnUrl);
        Assert.Equal("on", r.Programmatic);

        var param = r.SpawnUrl!["http://localhost:5173#/es/new?s=".Length..];
        using var doc = JsonDocument.Parse(B64.Decode(param));
        Assert.Equal("on", doc.RootElement.GetProperty("programmatic").GetString());
        Assert.Equal("http://127.0.0.1:5599/?c=corr-fixed", doc.RootElement.GetProperty("resultCallbackUrl").GetString());
    }

    [Fact]
    public void Omits_callback_when_receiver_is_off()
    {
        var r = Build("http://localhost:5173", null);
        Assert.Null(r.Errors);
        Assert.False(r.CallbackWired);
        Assert.Null(r.SpawnId);
        Assert.NotNull(r.SpawnUrl);

        var param = r.SpawnUrl!["http://localhost:5173#/es/new?s=".Length..];
        using var doc = JsonDocument.Parse(B64.Decode(param));
        Assert.False(doc.RootElement.TryGetProperty("resultCallbackUrl", out _));
    }

    [Fact]
    public void Fails_cleanly_on_bad_base_url_and_invalid_schema()
    {
        var noBase = Build(null, 5599);
        Assert.Null(noBase.SpawnUrl);
        Assert.NotNull(noBase.Errors);

        var noHidden = ScrollSeedTools.Build("http://localhost:5173", 5599, "c", "t", "p", "g", "solve",
            [new SeedTestCase("a", "b", false)], 1000);
        Assert.Null(noHidden.SpawnUrl);
        Assert.Contains(noHidden.Errors!, e => e.Contains("hidden"));

        var noTests = ScrollSeedTools.Build("http://localhost:5173", 5599, "c", "t", "p", "g", "solve", [], 1000);
        Assert.Null(noTests.SpawnUrl);
        Assert.NotNull(noTests.Errors);
    }
}
