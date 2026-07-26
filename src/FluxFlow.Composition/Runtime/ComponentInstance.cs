using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed class ComponentInstance
{
    private readonly Func<ValueTask>? _disposeAsync;
    private readonly Dictionary<string, ComponentOutputPort> _outputs;
    private ComponentEventBridge? _eventBridge;

    public ComponentInstance(
        IFlowNode node,
        IEnumerable<ComponentInputPort>? inputs = null,
        IEnumerable<ComponentOutputPort>? outputs = null,
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

    public IReadOnlyDictionary<string, ComponentInputPort> Inputs { get; }

    public IReadOnlyDictionary<string, ComponentOutputPort> Outputs { get; }

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
        if (_outputs.ContainsKey(ComponentEvents.PortName))
        {
            throw new InvalidOperationException(
                $"Output port '{ComponentEvents.PortName}' is reserved for component events.");
        }

        _eventBridge = new ComponentEventBridge(
            workflowName,
            componentName,
            Events,
            Completion);
        _outputs.Add(
            ComponentEvents.PortName,
            ComponentPorts.Output(ComponentEvents.PortName, _eventBridge.Output));
    }

    public static ComponentInstance Create(
        IFlowNode node,
        IEnumerable<ComponentInputPort>? inputs = null,
        IEnumerable<ComponentOutputPort>? outputs = null,
        ISourceBlock<FlowEvent>? events = null,
        Task? completion = null,
        Func<ValueTask>? disposeAsync = null)
        => new(node, inputs, outputs, events, completion, disposeAsync);

    private static IReadOnlyDictionary<string, ComponentInputPort> ToInputDictionary(
        IEnumerable<ComponentInputPort>? ports)
    {
        var result = new Dictionary<string, ComponentInputPort>(StringComparer.Ordinal);
        if (ports is null)
            return result;

        foreach (var port in ports)
        {
            if (!result.TryAdd(port.Name, port))
                throw new ArgumentException($"Duplicate input port name '{port.Name}'.", nameof(ports));
        }

        return result;
    }

    private static Dictionary<string, ComponentOutputPort> ToOutputDictionary(
        IEnumerable<ComponentOutputPort>? ports)
    {
        var result = new Dictionary<string, ComponentOutputPort>(StringComparer.Ordinal);
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
