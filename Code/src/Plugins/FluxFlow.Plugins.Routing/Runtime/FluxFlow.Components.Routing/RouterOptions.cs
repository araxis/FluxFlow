using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Routing;

public sealed class RouterOptions
{
    public required IReadOnlyList<Predicate<FlowMessage>> Routes { get; init; }
    public ExecutionDataflowBlockOptions? Execution { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
