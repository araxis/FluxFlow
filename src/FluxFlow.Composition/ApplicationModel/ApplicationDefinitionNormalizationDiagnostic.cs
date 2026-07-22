namespace FluxFlow.Composition.Model;

public sealed record ApplicationDefinitionNormalizationDiagnostic
{
    public required string Code { get; init; }

    public required ApplicationDefinitionNormalizationDiagnosticKind Kind { get; init; }

    public required string Path { get; init; }

    public required string PreviousType { get; init; }

    public required string CanonicalType { get; init; }

    public required string Message { get; init; }
}
