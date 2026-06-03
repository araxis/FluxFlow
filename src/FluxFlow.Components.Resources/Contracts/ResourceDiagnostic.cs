namespace FluxFlow.Components.Resources.Contracts;

public sealed record ResourceDiagnostic
{
    public required ResourceDiagnosticCode Code { get; init; }
    public required ResourceDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public ResourceName? Name { get; init; }
    public string? Kind { get; init; }
    public ResourceReference? Reference { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
