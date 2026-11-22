using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.ChangeFilter;

public sealed class ChangeFilterOptions<TKey> where TKey : notnull
{
    public required Func<FlowMessage, TKey> KeySelector { get; init; }
    public Func<TKey, TKey, bool> AreEqual { get; init; } = EqualityComparer<TKey>.Default.Equals;
    public ExecutionDataflowBlockOptions? Execution { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
