using System.Text.Json;
using PersonalServer.Data;

namespace PersonalServer.Vault;

/// <summary>
/// Selects the experience-store backend PersonalServer runs against. STARfolio owns the choice and
/// pushes it into a config file (see ../SuperSTAR/docs/personalserver-config-handshake.md); this
/// reads that file. Precedence: env vars (EXPERIENCE_BACKEND / EXPERIENCE_VAULT / SUPERSTAR_DB_PATH)
/// override the config file, which overrides the built-in default (vault). The precedence logic in
/// <see cref="Resolve"/> is pure so it is unit-testable without env or filesystem.
/// </summary>
internal static class BackendConfig
{
    public const string BackendEnv = "EXPERIENCE_BACKEND";
    public const string ConfigFileEnv = "PERSONALSERVER_CONFIG_FILE";

    public sealed record Choice(string Backend, string? VaultPath, string? DbPath);

    public static Choice Resolve(IReadOnlyDictionary<string, string?> env, string? configJson)
    {
        var file = ParseConfig(configJson);
        var backend = FirstNonBlank(Get(env, BackendEnv), file?.Backend);
        var vaultPath = FirstNonBlank(Get(env, VaultStore.EnvVar), file?.VaultPath);
        var dbPath = FirstNonBlank(Get(env, Db.PathEnvVar), file?.DbPath);
        var resolved = string.Equals(backend, "sqlite", StringComparison.OrdinalIgnoreCase) ? "sqlite" : "vault";
        return new Choice(resolved, vaultPath, dbPath);
    }

    public static (string? Backend, string? VaultPath, string? DbPath)? ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            return (Str(root, "backend"), Str(root, "vaultPath"), Str(root, "dbPath"));
        }
        catch (JsonException) { return null; }
    }

    public static IExperienceStore Select()
    {
        var env = new Dictionary<string, string?>
        {
            [BackendEnv] = Environment.GetEnvironmentVariable(BackendEnv),
            [VaultStore.EnvVar] = Environment.GetEnvironmentVariable(VaultStore.EnvVar),
            [Db.PathEnvVar] = Environment.GetEnvironmentVariable(Db.PathEnvVar),
        };
        string? json = null;
        try
        {
            var path = ConfigFilePath();
            if (File.Exists(path)) json = File.ReadAllText(path);
        }
        catch { /* unreadable config is a clean fall-through to defaults */ }

        var choice = Resolve(env, json);
        return choice.Backend == "sqlite" ? new SqliteStore(choice.DbPath) : new VaultStore(choice.VaultPath);
    }

    public static void Apply(TextWriter log)
    {
        var store = Select();
        ExperienceStore.Current = store;
        log.WriteLine($"[PersonalServer] experience backend: {(store is SqliteStore ? "sqlite" : "vault")}");
    }

    static string ConfigFilePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(ConfigFileEnv);
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        string dir;
        if (OperatingSystem.IsWindows())
            dir = FirstNonBlank(Environment.GetEnvironmentVariable("LOCALAPPDATA"))
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
        else if (OperatingSystem.IsMacOS())
            dir = Path.Combine(Home(), "Library", "Application Support");
        else
            dir = FirstNonBlank(Environment.GetEnvironmentVariable("XDG_DATA_HOME"))
                  ?? Path.Combine(Home(), ".local", "share");
        return Path.Combine(dir, "PersonalServer", "config.json");
    }

    static string Home() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    static string? Get(IReadOnlyDictionary<string, string?> env, string key)
        => env.TryGetValue(key, out var v) ? v : null;

    static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    static string? Str(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
