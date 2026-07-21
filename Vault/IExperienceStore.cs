namespace PersonalServer.Vault;

internal interface IExperienceStore
{
    (IReadOnlyList<Experience> Items, string? Error) LoadAll();

    IReadOnlyList<string> KnownEntities();

    (string Id, string Path, string? Error) Write(
        string id, string title, string context, string status,
        (string Beat, string Text, string Confidence)[] beats,
        IReadOnlyList<VaultSkill> skills, IReadOnlyList<string> tags,
        IReadOnlyList<VaultMetric> metrics, IReadOnlyList<string> entities,
        IReadOnlyList<string> gaps);
}

internal static class ExperienceStore
{
    static IExperienceStore? _current;

    public static IExperienceStore Current
    {
        get => _current ??= new VaultStore();
        set => _current = value;
    }
}
