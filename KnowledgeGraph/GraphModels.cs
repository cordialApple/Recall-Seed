namespace PersonalServer.KnowledgeGraph;

public record MetricDto(string Label, double? Value, string? Unit);

public record ExperienceCard(
    string Id,
    string Title,
    string Context,
    string Status,
    string? HappenedStart,
    string? HappenedEnd,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Tags,
    IReadOnlyList<MetricDto> Metrics);

public record QueryResult(IReadOnlyList<ExperienceCard> Experiences, string? Error = null);

public record NodeHit(string NodeType, string Id, string Title, string? Snippet);

public record SearchResult(IReadOnlyList<NodeHit> Nodes, string? Error = null);

public record NeighborEntity(string Id, string Kind, string Name);

public record Connection(
    string ExperienceId,
    string Title,
    IReadOnlyList<string> ViaEntities,
    IReadOnlyList<string> ViaSkills);

public record NeighborsResult(
    IReadOnlyList<NeighborEntity> Entities,
    IReadOnlyList<Connection> Connections,
    string? Error = null);

public record SchemaResult(
    IReadOnlyList<string> EntityKinds,
    IReadOnlyList<string> EdgeRelations,
    IReadOnlyList<string> DerivedRelations,
    IReadOnlyList<string> ExperienceFields,
    IReadOnlyList<string> ReadTools,
    IReadOnlyList<string> WriteTools,
    IReadOnlyList<string> ContractViews,
    IReadOnlyList<string> MissingViews,
    string? Error = null);
