using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Batch;

public sealed class BatchOptions
{
    public required int BatchSize { get; init; }
    /// <summary>Maximum collection time from the first ordinary message, not an inactivity timeout.</summary>
    public required TimeSpan MaxWait { get; init; }
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    /// <summary>Batch requires ordered serial input processing.</summary>
    public ExecutionDataflowBlockOptions? Execution { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
