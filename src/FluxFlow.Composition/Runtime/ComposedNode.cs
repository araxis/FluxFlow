using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed class ComposedNode
{
    private readonly Func<ValueTask>? _disposeAsync;

    public ComposedNode(
        IFlowNode node,
        IEnumerable<CompositionInputPort>? inputs = null,
        IEnumerable<CompositionOutputPort>? outputs = null,
        ISourceBlock<FlowEvent>? events = null,
        ISourceBlock<FlowError>? errors = null,
        Task? completion = null,
        Func<ValueTask>? disposeAsync = null)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Inputs = ToInputDictionary(inputs);
        Outputs = ToOutputDictionary(outputs);
        Events = events;
        Errors = errors;
        Completion = completion ?? node.Completion;
        _disposeAsync = disposeAsync;
    }

    public IFlowNode Node { get; }

    public IReadOnlyDictionary<string, CompositionInputPort> Inputs { get; }

    public IReadOnlyDictionary<string, CompositionOutputPort> Outputs { get; }

    public ISourceBlock<FlowEvent>? Events { get; }

    public ISourceBlock<FlowError>? Errors { get; }

    public Task Completion { get; }

    public async ValueTask DisposeAsync()
    {
        await Node.DisposeAsync().ConfigureAwait(false);
        if (_disposeAsync is not null)
            await _disposeAsync().ConfigureAwait(false);
    }

    public static ComposedNode Create(
        IFlowNode node,
        IEnumerable<CompositionInputPort>? inputs = null,
        IEnumerable<CompositionOutputPort>? outputs = null,
        ISourceBlock<FlowEvent>? events = null,
        ISourceBlock<FlowError>? errors = null,
        Task? completion = null,
        Func<ValueTask>? disposeAsync = null)
        => new(node, inputs, outputs, events, errors, completion, disposeAsync);

    private static IReadOnlyDictionary<string, CompositionInputPort> ToInputDictionary(
        IEnumerable<CompositionInputPort>? ports)
    {
        var result = new Dictionary<string, CompositionInputPort>(StringComparer.Ordinal);
        if (ports is null)
            return result;

        foreach (var port in ports)
        {
            if (!result.TryAdd(port.Name, port))
                throw new ArgumentException($"Duplicate input port name '{port.Name}'.", nameof(ports));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, CompositionOutputPort> ToOutputDictionary(
        IEnumerable<CompositionOutputPort>? ports)
    {
        var result = new Dictionary<string, CompositionOutputPort>(StringComparer.Ordinal);
        if (ports is null)
            return result;

        foreach (var port in ports)
        {
            if (!result.TryAdd(port.Name, port))
                throw new ArgumentException($"Duplicate output port name '{port.Name}'.", nameof(ports));
        }

        return result;
    }
}
