using Recall_Seed.Vault;

namespace Recall_Seed.Tests;

public class BackendConfigTests
{
    static Dictionary<string, string?> Env(params (string, string?)[] pairs)
        => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    [Fact]
    public void No_env_no_config_defaults_to_vault()
    {
        var c = BackendConfig.Resolve(Env(), null);
        Assert.Equal("vault", c.Backend);
        Assert.Null(c.VaultPath);
        Assert.Null(c.DbPath);
    }

    [Fact]
    public void Config_selects_sqlite_with_dbPath()
    {
        var c = BackendConfig.Resolve(Env(), """{"version":1,"backend":"sqlite","vaultPath":null,"dbPath":"/x/superstar.db","source":"starfolio"}""");
        Assert.Equal("sqlite", c.Backend);
        Assert.Equal("/x/superstar.db", c.DbPath);
    }

    [Fact]
    public void Config_selects_vault_with_vaultPath()
    {
        var c = BackendConfig.Resolve(Env(), """{"version":1,"backend":"vault","vaultPath":"/v/Design_Exp","dbPath":"/x/superstar.db","source":"starfolio"}""");
        Assert.Equal("vault", c.Backend);
        Assert.Equal("/v/Design_Exp", c.VaultPath);
    }

    [Fact]
    public void Env_backend_overrides_config()
    {
        var c = BackendConfig.Resolve(Env((BackendConfig.BackendEnv, "vault")), """{"backend":"sqlite","dbPath":"/x/superstar.db"}""");
        Assert.Equal("vault", c.Backend);
    }

    [Fact]
    public void Env_vault_path_overrides_config()
    {
        var c = BackendConfig.Resolve(Env((VaultStore.EnvVar, "/env/vault")), """{"backend":"vault","vaultPath":"/cfg/vault"}""");
        Assert.Equal("/env/vault", c.VaultPath);
    }

    [Fact]
    public void Env_db_path_overrides_config()
    {
        var c = BackendConfig.Resolve(Env((Recall_Seed.Data.Db.PathEnvVar, "/env/superstar.db")), """{"backend":"sqlite","dbPath":"/cfg/superstar.db"}""");
        Assert.Equal("/env/superstar.db", c.DbPath);
    }

    [Fact]
    public void Unknown_backend_string_falls_back_to_vault()
    {
        var c = BackendConfig.Resolve(Env(), """{"backend":"obsidian"}""");
        Assert.Equal("vault", c.Backend);
    }

    [Fact]
    public void Db_present_but_no_backend_key_resolves_to_vault()
    {
        var c = BackendConfig.Resolve(Env(), """{"version":1,"vaultPath":null,"dbPath":"/x/superstar.db","source":"starfolio"}""");
        Assert.Equal("vault", c.Backend);
        Assert.Equal("/x/superstar.db", c.DbPath);
    }

    [Fact]
    public void Malformed_config_is_ignored()
    {
        var c = BackendConfig.Resolve(Env(), "not json {{{");
        Assert.Equal("vault", c.Backend);
        Assert.Null(BackendConfig.ParseConfig("not json"));
        Assert.Null(BackendConfig.ParseConfig(""));
        Assert.Null(BackendConfig.ParseConfig("[1,2,3]"));
    }

    [Fact]
    public void Blank_env_does_not_override_config()
    {
        var c = BackendConfig.Resolve(Env((BackendConfig.BackendEnv, "  ")), """{"backend":"sqlite","dbPath":"/x.db"}""");
        Assert.Equal("sqlite", c.Backend);
    }
}
