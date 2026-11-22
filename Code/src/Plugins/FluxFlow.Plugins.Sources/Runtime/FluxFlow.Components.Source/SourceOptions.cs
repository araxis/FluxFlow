using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Source;

public sealed class SourceOptions
{
    public required IReadOnlyList<FlowValue> Values { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
