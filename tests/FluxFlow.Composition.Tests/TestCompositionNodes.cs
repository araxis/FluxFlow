using FluxFlow.Nodes;

namespace FluxFlow.Composition.Tests;

internal static class TestNodeTypes
{
    public const string Source = "test.source";
    public const string TickingSource = "test.ticking-source";
    public const string IntSource = "test.int-source";
    public const string Uppercase = "test.uppercase";
    public const string Sink = "test.sink";
    public const string TrackedSource = "test.tracked-source";
    public const string Failing = "test.failing";
}

internal sealed class StringCollector
{
    private readonly List<string> _items = [];

    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_items)
            {
                return _items.ToArray();
            }
        }
    }

    public void Add(string item)
    {
        lock (_items)
        {
            _items.Add(item);
        }
    }
}

internal sealed class TestServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    public TestServiceProvider Add<TService>(TService service)
        where TService : notnull
    {
        _services[typeof(TService)] = service;
        return this;
    }

    public object? GetService(Type serviceType)
        => _services.TryGetValue(serviceType, out var service) ? service : null;
}

internal static class TestCompositionRegistry
{
    public static CompositionNodeRegistry Create(BuildTracker? tracker = null)
    {
        tracker ??= new BuildTracker();

        return new CompositionNodeRegistry()
            .Register(
                TestNodeTypes.Source,
                context =>
                {
                    var options = context.BindConfiguration<SourceOptions>();
                    var node = new StringSourceNode(options.Messages);
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        outputs: [CompositionPorts.Output<string>("Output", node.Output)],
                        events: node.Events,
                        errors: node.Errors));
                },
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                TestNodeTypes.TickingSource,
                _ =>
                {
                    var node = new TickingSourceNode();
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        outputs: [CompositionPorts.Output<string>("Output", node.Output)],
                        events: node.Events,
                        errors: node.Errors));
                },
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                TestNodeTypes.IntSource,
                _ =>
                {
                    var node = new IntSourceNode();
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        outputs: [CompositionPorts.Output<int>("Output", node.Output)],
                        events: node.Events,
                        errors: node.Errors));
                },
                outputs: [CompositionPorts.Metadata<int>("Output")])
            .Register(
                TestNodeTypes.Uppercase,
                _ =>
                {
                    var node = new UppercaseNode();
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        inputs: [CompositionPorts.Input<string>("Input", node.Input)],
                        outputs: [CompositionPorts.Output<string>("Output", node.Output)],
                        events: node.Events,
                        errors: node.Errors));
                },
                inputs: [CompositionPorts.Metadata<string>("Input")],
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                TestNodeTypes.Sink,
                context =>
                {
                    var collector = (StringCollector?)context.Services.GetService(typeof(StringCollector))
                        ?? new StringCollector();
                    var node = new CollectSinkNode(collector);
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        inputs: [CompositionPorts.Input<string>("Input", node.Input)],
                        events: node.Events,
                        errors: node.Errors));
                },
                inputs: [CompositionPorts.Metadata<string>("Input")])
            .Register(
                TestNodeTypes.TrackedSource,
                _ =>
                {
                    var node = new TrackedSourceNode(tracker);
                    return ValueTask.FromResult(ComposedNode.Create(
                        node,
                        outputs: [CompositionPorts.Output<string>("Output", node.Output)],
                        events: node.Events,
                        errors: node.Errors));
                },
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                TestNodeTypes.Failing,
                _ => throw new InvalidOperationException("factory failed"));
    }
}

internal sealed record SourceOptions
{
    public string[] Messages { get; init; } = [];
}

internal sealed class BuildTracker
{
    public int DisposedNodes { get; private set; }

    public void MarkDisposed() => DisposedNodes++;
}

internal sealed class StringSourceNode(IReadOnlyList<string> messages) : FlowSource<string>
{
    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Emit(FlowMessage.Create(message));
        }

        return Task.CompletedTask;
    }
}

internal sealed class TickingSourceNode : FlowSource<string>
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Emit(FlowMessage.Create("tick"));
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}

internal sealed class IntSourceNode : FlowSource<int>
{
    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        Emit(FlowMessage.Create(1));
        return Task.CompletedTask;
    }
}

internal sealed class TrackedSourceNode(BuildTracker tracker) : FlowSource<string>
{
    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        Emit(FlowMessage.Create("tracked"));
        return Task.CompletedTask;
    }

    protected override ValueTask OnDisposeAsync()
    {
        tracker.MarkDisposed();
        return ValueTask.CompletedTask;
    }
}

internal sealed class UppercaseNode : FlowNode<string, string>
{
    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        Emit(message.With(message.Payload.ToUpperInvariant()));
        return Task.CompletedTask;
    }
}

internal sealed class CollectSinkNode(StringCollector collector) : FlowNode<string, string>
{
    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        collector.Add(message.Payload);
        Emit(message);
        return Task.CompletedTask;
    }
}
