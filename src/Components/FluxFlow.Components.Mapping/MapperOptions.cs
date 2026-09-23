using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Mapping;

/// <summary>Construction options. The mapping delegate is executable code, not JSON configuration.</summary>
public sealed class MapperOptions
{
    public required Func<FlowMessage, CancellationToken, ValueTask<FlowValue>> MapAsync { get; init; }
    public ExecutionDataflowBlockOptions? Execution { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
