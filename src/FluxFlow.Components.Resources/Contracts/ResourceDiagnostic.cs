namespace FluxFlow.Components.Resources.Contracts;

public sealed record ResourceDiagnostic
{
    private IReadOnlyDictionary<string, string> _metadata = new Dictionary<string, string>(StringComparer.Ordinal);

    public required ResourceDiagnosticCode Code { get; init; }
    public required ResourceDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public ResourceName? Name { get; init; }
    public string? Kind { get; init; }
    public ResourceReference? Reference { get; init; }
    public IReadOnlyDictionary<string, string> Metadata
    {
        get => _metadata;
        init => _metadata = ResourceContractMap.Copy(value);
    }

    public override string ToString() => $"{Severity} {Code}: {Message}";
}
