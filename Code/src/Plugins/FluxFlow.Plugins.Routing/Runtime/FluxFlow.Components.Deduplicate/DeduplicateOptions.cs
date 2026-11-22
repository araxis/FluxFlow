using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Deduplicate;

public sealed class DeduplicateOptions<TKey> where TKey : notnull
{
    public required Func<FlowMessage, TKey> KeySelector { get; init; }
    public IEqualityComparer<TKey> Comparer { get; init; } = EqualityComparer<TKey>.Default;
    /// <summary>Retention from the first forwarded occurrence; duplicates do not extend it.</summary>
    public required TimeSpan Retention { get; init; }
    /// <summary>Oldest remembered key is evicted when this capacity is reached.</summary>
    public int MaxKeys { get; init; } = 1024;
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public ExecutionDataflowBlockOptions? Execution { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
