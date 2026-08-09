using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Model;
using FluxFlow.Nodes;

namespace FluxFlow.Fluent;

/// <summary>
/// Collects the fluent chain into the same immutable application definition and component
/// contracts used by the general code-first authoring API.
/// </summary>
internal sealed class FlowGraphBuilder
{
    private const string InputPort = "Input";
    private const string OutputPort = "Output";
    private const string EventsPort = "Events";

    private readonly ApplicationDefinitionBuilder _application = new();
    private readonly WorkflowDefinitionBuilder _workflow;
    private readonly Dictionary<IFlowNode, object> _nodes =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, object> _branches =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<ISourceBlock<FlowEvent>> _eventSources = [];
    private readonly List<Action<FlowEvent>> _eventHandlers = [];
    private int _nextComponent;

    internal FlowGraphBuilder()
    {
        _workflow = _application.AddWorkflow("main");
    }

    internal FluentSourceHandle<T> RegisterSource<T>(FlowSource<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_nodes.TryGetValue(source, out var existing))
            return (FluentSourceHandle<T>)existing;

        var identity = NextIdentity();
        var contract = ComponentContract.Create<FluentSourceHandle<T>>(
            identity.Type,
            runtime => runtime
                .UseFactory(_ => source)
                .HasOutput(OutputPort, static node => node.Output)
                .HasEvents(EventsPort, static node => node.Events),
            static component => new FluentSourceHandle<T>(component));
        var handle = _workflow.AddComponent(identity.Name, contract);
        _nodes.Add(source, handle);
        _eventSources.Add(source.Events);
        return handle;
    }

    internal FluentNodeHandle<TInput, TOutput> RegisterNode<TInput, TOutput>(
        FlowNode<TInput, TOutput> node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (_nodes.TryGetValue(node, out var existing))
            return (FluentNodeHandle<TInput, TOutput>)existing;

        var identity = NextIdentity();
        var contract = ComponentContract.Create<FluentNodeHandle<TInput, TOutput>>(
            identity.Type,
            runtime => runtime
                .UseFactory(_ => node)
                .HasInput(InputPort, static value => value.Input)
                .HasOutput(OutputPort, static value => value.Output)
                .HasEvents(EventsPort, static value => value.Events),
            static component => new FluentNodeHandle<TInput, TOutput>(component));
        var handle = _workflow.AddComponent(identity.Name, contract);
        _nodes.Add(node, handle);
        _eventSources.Add(node.Events);
        return handle;
    }

    internal FluentBranchHandle<T> RegisterBranch<T>(ISourceBlock<FlowMessage<T>> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_branches.TryGetValue(source, out var existing))
            return (FluentBranchHandle<T>)existing;

        var adapter = new BorrowedOutputNode<T>(source);
        var identity = NextIdentity();
        var contract = ComponentContract.Create<FluentBranchHandle<T>>(
            identity.Type,
            runtime => runtime
                .UseFactory(_ => adapter)
                .HasSignalInput("Dependency", static node => node)
                .HasOutput(OutputPort, static node => node.Output),
            static component => new FluentBranchHandle<T>(component));
        var handle = _workflow.AddComponent(identity.Name, contract);
        _branches.Add(source, handle);
        return handle;
    }

    internal void Connect<T>(OutputPortHandle<T> source, InputPortHandle<T> target)
        => _workflow.Connect(source, target);

    internal void Connect<T>(OutputPortHandle<T> source, SignalInputPortHandle target)
        => _workflow.Connect(source, target);

    internal void OnEvent(Action<FlowEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _eventHandlers.Add(handler);
    }

    internal FlowGraph Build()
    {
        if (_nodes.Count == 0)
            throw new InvalidOperationException("A flow must start from at least one source (Flow.From).");

        var graph = new FlowGraph(
            _application.Build(),
            _eventSources,
            _nodes.Keys
                .OfType<IFlowSource>()
                .Select(static source => source.Completion)
                .ToArray());
        foreach (var handler in _eventHandlers)
            graph.OnEvent(handler);
        return graph;
    }

    private (string Name, string Type) NextIdentity()
    {
        var id = ++_nextComponent;
        return ($"node{id:D4}", $"fluent.node.{id:D4}");
    }

}

internal sealed class FluentSourceHandle<T> : AuthoredComponentHandle
{
    internal FluentSourceHandle(ComponentHandle definition) : base(definition)
    {
        Output = definition.Output<T>("Output");
        Events = definition.Output<ComponentEvent>("Events");
    }

    internal OutputPortHandle<T> Output { get; }

    internal OutputPortHandle<ComponentEvent> Events { get; }
}

internal sealed class FluentNodeHandle<TInput, TOutput> : AuthoredComponentHandle
{
    internal FluentNodeHandle(ComponentHandle definition) : base(definition)
    {
        Input = definition.Input<TInput>("Input");
        Output = definition.Output<TOutput>("Output");
        Events = definition.Output<ComponentEvent>("Events");
    }

    internal InputPortHandle<TInput> Input { get; }

    internal OutputPortHandle<TOutput> Output { get; }

    internal OutputPortHandle<ComponentEvent> Events { get; }
}

internal sealed class FluentBranchHandle<T> : AuthoredComponentHandle
{
    internal FluentBranchHandle(ComponentHandle definition) : base(definition)
    {
        Dependency = definition.SignalInput("Dependency");
        Output = definition.Output<T>("Output");
    }

    internal SignalInputPortHandle Dependency { get; }

    internal OutputPortHandle<T> Output { get; }
}

internal sealed class BorrowedOutputNode<T>(ISourceBlock<FlowMessage<T>> output) :
    IFlowNode,
    IFlowSignalTarget
{
    internal ISourceBlock<FlowMessage<T>> Output { get; } =
        output ?? throw new ArgumentNullException(nameof(output));

    public Task Completion => Output.Completion;

    public void Complete() { }

    public void Fault(Exception exception) => ArgumentNullException.ThrowIfNull(exception);

    public ValueTask<bool> SendAsync<TSignal>(
        FlowMessage<TSignal> signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
