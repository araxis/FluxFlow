namespace FluxFlow.Components.Secrets.Contracts;

public sealed record SecretDiagnostic
{
    public required SecretDiagnosticCode Code { get; init; }
    public required SecretDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public SecretName? Name { get; init; }
    public string? Version { get; init; }
    public string? Kind { get; init; }
    public SecretReference? Reference { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public override string ToString() => $"{Severity} {Code}: {Message}";
}
