using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Reducer;

public sealed class ReducerOptions
{
    public required FlowValue InitialState { get; init; }
    /// <summary>Pure reduction: return an immutable candidate without mutating either argument.</summary>
    public required Func<FlowValue, FlowValue, FlowValue> Reduce { get; init; }
    /// <summary>Defines state equality explicitly; no reference or JSON equality is assumed.</summary>
    public required Func<FlowValue, FlowValue, bool> AreEqual { get; init; }
    /// <summary>Reducer requires ordered, single-message processing.</summary>
    public ExecutionDataflowBlockOptions? Execution { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
