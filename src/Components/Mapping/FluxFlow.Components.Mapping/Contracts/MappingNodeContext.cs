using FluxFlow.Components.Mapping.Options;

namespace FluxFlow.Components.Mapping.Contracts;

/// <summary>
/// Per-node context handed to an <see cref="IMappingContextFactory"/> so it can
/// build a <see cref="FluxFlow.Mapping.FlowMapContext"/> for each message. Carries the
/// node's resolved options and the concrete input/output types it was constructed for.
/// </summary>
public sealed record MappingNodeContext
{
    public required MapperOptions Options { get; init; }
    public required Type InputType { get; init; }
    public required Type OutputType { get; init; }
}
