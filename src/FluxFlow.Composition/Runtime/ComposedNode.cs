using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed class ComposedNode
{
    private readonly Func<ValueTask>? _disposeAsync;
    private readonly Dictionary<string, CompositionOutputPort> _outputs;
    private CompositionComponentEventBridge? _eventBridge;

    public ComposedNode(
        IFlowNode node,
        IEnumerable<CompositionInputPort>? inputs = null,
        IEnumerable<CompositionOutputPort>? outputs = null,
        ISourceBlock<FlowEvent>? events = null,
        Task? completion = null,
        Func<ValueTask>? disposeAsync = null)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Inputs = ToInputDictionary(inputs);
        _outputs = ToOutputDictionary(outputs);
        Outputs = _outputs;
        Events = events;
        Completion = completion ?? node.Completion;
        _disposeAsync = disposeAsync;
    }

    public IFlowNode Node { get; }

    public IReadOnlyDictionary<string, CompositionInputPort> Inputs { get; }

    public IReadOnlyDictionary<string, CompositionOutputPort> Outputs { get; }

    public ISourceBlock<FlowEvent>? Events { get; }

    public Task Completion { get; }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        try
        {
            await Node.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (_disposeAsync is not null)
        {
            try
            {
                await _disposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (_eventBridge is not null)
        {
            try
            {
                await _eventBridge.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count == 0)
            return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();

        throw new AggregateException("Composed component cleanup failed.", failures);
    }

    internal void AttachAddressableEvents(string workflowName, string componentName)
    {
        if (_eventBridge is not null)
            throw new InvalidOperationException("Addressable component events are already attached.");
        if (_outputs.ContainsKey(CompositionComponentEvents.PortName))
        {
            throw new InvalidOperationException(
                $"Output port '{CompositionComponentEvents.PortName}' is reserved for component events.");
        }

        _eventBridge = new CompositionComponentEventBridge(
            workflowName,
            componentName,
            Events,
            Completion);
        _outputs.Add(
            CompositionComponentEvents.PortName,
            CompositionPorts.Output(CompositionComponentEvents.PortName, _eventBridge.Output));
    }

    public static ComposedNode Create(
        IFlowNode node,
        IEnumerable<CompositionInputPort>? inputs = null,
        IEnumerable<CompositionOutputPort>? outputs = null,
        ISourceBlock<FlowEvent>? events = null,
        Task? completion = null,
        Func<ValueTask>? disposeAsync = null)
        => new(node, inputs, outputs, events, completion, disposeAsync);

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

    private static Dictionary<string, CompositionOutputPort> ToOutputDictionary(
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
