using FluxFlow.Composition.Addressing;

namespace FluxFlow.Composition.Links;

public sealed record ApplicationLinkDiagnostic
{
    public required ApplicationLinkDiagnosticCode Code { get; init; }

    public required string Message { get; init; }

    public string? WorkflowName { get; init; }

    public string? ComponentName { get; init; }

    public string? PropertyName { get; init; }

    public ApplicationAddress? Source { get; init; }

    public ApplicationAddress? Target { get; init; }

    public Exception? Exception { get; init; }

    public override string ToString() => Message;
}
