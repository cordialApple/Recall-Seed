using System.ComponentModel;

namespace PersonalServer.Capture;

public record AddEntityResult(string? Id, string? Error = null);

public record AddEdgeResult(string? Id, string? Error = null);

public record MetricInput(
    [property: Description("Metric label, e.g. 'downtime' or 'revenue'.")] string Label,
    [property: Description("Numeric value, if any.")] double? Value = null,
    [property: Description("Unit, e.g. 'min', '%', 'USD'.")] string? Unit = null);

public record CaptureResult(string? Id, string? Note = null, string? Error = null);
