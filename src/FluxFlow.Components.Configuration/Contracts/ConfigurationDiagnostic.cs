namespace FluxFlow.Components.Configuration.Contracts;

public sealed record ConfigurationDiagnostic
{
    public required ConfigurationDiagnosticSource Source { get; init; }
    public required string Code { get; init; }
    public required ConfigurationDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public string? Path { get; init; }
    public string? Name { get; init; }
    public string? Kind { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public override string ToString()
        => string.IsNullOrWhiteSpace(Path)
            ? $"{Severity} {Source}.{Code}: {Message}"
            : $"{Severity} {Source}.{Code} at {Path}: {Message}";
}
