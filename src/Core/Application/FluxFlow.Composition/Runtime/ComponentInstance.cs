using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed class ComponentInstance
{
    private readonly Func<ValueTask>? _disposeAsync;
    private readonly IReadOnlyList<ComponentEventSource> _addressableEvents;
    private readonly Dictionary<string, ComponentOutputPort> _outputs;
    private readonly List<ComponentActivityBridge> _activityBridges = [];
    private bool _eventsAttached;
    private readonly BroadcastBlock<FlowMessage> _activity = new(static message => message,
        new DataflowBlockOptions { BoundedCapacity = 256 });
    private string? _workflowName;
    private string? _componentName;

    public ComponentInstance(
        IFlowNode node,
        IEnumerable<ComponentInputPort>? inputs = null,
        IEnumerable<ComponentOutputPort>? outputs = null,
        ISourceBlock<FlowMessage>? events = null,
        Task? completion = null,
        Func<ValueTask>? disposeAsync = null,
        IEnumerable<ComponentEventSource>? addressableEvents = null)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Inputs = ToInputDictionary(inputs);
        _outputs = ToOutputDictionary(outputs);
        Outputs = _outputs;
        Events = events;
        _addressableEvents = ToEventSources(addressableEvents);
        Completion = completion ?? node.Completion;
        _disposeAsync = disposeAsync;
    }

    public IFlowNode Node { get; }

    public IReadOnlyDictionary<string, ComponentInputPort> Inputs { get; }

    public IReadOnlyDictionary<string, ComponentOutputPort> Outputs { get; }

    public ISourceBlock<FlowMessage>? Events { get; }

    public Task Completion { get; }

    /// <summary>Native, best-effort observations from named and legacy component event sources.</summary>
    public ISourceBlock<FlowMessage> Activity => _activity;

    internal void NotifyEvent(FlowMessage message)
    {
        if (_workflowName is not null && !message.Headers.ContainsKey(FlowEventHeaders.Source))
        {
            var headers = message.Headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            headers[FlowEventHeaders.Workflow] = _workflowName;
            headers[FlowEventHeaders.Component] = _componentName!;
            headers[FlowEventHeaders.Source] = $"{_workflowName}.{_componentName}";
            message = message.IsError
                ? FlowMessage.RestoreError(message.Error!, message.MessageId, message.TraceId, message.Timestamp,
                    message.CorrelationId, message.CausationId, headers)
                : FlowMessage.Restore(message.Value!, message.MessageId, message.TraceId, message.Timestamp,
                    message.CorrelationId, message.CausationId, headers);
        }
        _activity.Post(message);
    }

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

        foreach (var activityBridge in _activityBridges)
        {
            try
            {
                await activityBridge.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        _activity.Complete();
        await _activity.Completion.ConfigureAwait(false);

        if (failures.Count == 0)
            return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();

        throw new AggregateException("Composed component cleanup failed.", failures);
    }

    internal void AttachAddressableEvents(string workflowName, string componentName)
    {
        if (_eventsAttached)
            throw new InvalidOperationException("Addressable component events are already attached.");
        _eventsAttached = true;
        _workflowName = workflowName;
        _componentName = componentName;

        foreach (var eventSource in _addressableEvents)
        {
            if (_outputs.ContainsKey(eventSource.Name))
            {
                throw new InvalidOperationException(
                    $"Output port '{eventSource.Name}' is already bound and cannot also be used for component events.");
            }

            var activityBridge = new ComponentActivityBridge(
                workflowName,
                componentName,
                eventSource.Source,
                Completion,
                NotifyEvent);
            _activityBridges.Add(activityBridge);
            _outputs.Add(eventSource.Name, ComponentPorts.FlowValueOutput(eventSource.Name, activityBridge.Output));
        }
    }

    public static ComponentInstance Create(
        IFlowNode node,
        IEnumerable<ComponentInputPort>? inputs = null,
        IEnumerable<ComponentOutputPort>? outputs = null,
        ISourceBlock<FlowMessage>? events = null,
        Task? completion = null,
        Func<ValueTask>? disposeAsync = null,
        IEnumerable<ComponentEventSource>? addressableEvents = null)
        => new(node, inputs, outputs, events, completion, disposeAsync, addressableEvents);

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

    private static IReadOnlyList<ComponentEventSource> ToEventSources(
        IEnumerable<ComponentEventSource>? sources)
    {
        if (sources is null)
            return [];

        var result = new List<ComponentEventSource>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!names.Add(source.Name))
                throw new ArgumentException($"Duplicate event port name '{source.Name}'.", nameof(sources));
            result.Add(source);
        }

        return result.ToArray();
    }
}
