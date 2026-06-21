namespace FluxFlow.Composition;

public sealed record CompositionDiagnostic
{
    public required CompositionDiagnosticCode Code { get; init; }

    public required string Message { get; init; }

    public string? WorkflowName { get; init; }

    public string? NodeName { get; init; }

    public LinkDefinition? Link { get; init; }

    public Exception? Exception { get; init; }

    public override string ToString() => Message;
}
