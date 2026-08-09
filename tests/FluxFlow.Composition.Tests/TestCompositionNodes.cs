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
    public static ComponentCatalog Create(BuildTracker? tracker = null)
    {
        tracker ??= new BuildTracker();

        return new ComponentCatalog(
        [
            new ComponentDescriptor(
                TestNodeTypes.Source,
                context =>
                {
                    var options = context.BindConfiguration<SourceOptions>();
                    var node = new StringSourceNode(options.Messages);
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        outputs: [ComponentPorts.Output<string>("Output", node.Output)],
                        addressableEvents: [ComponentPorts.Events("Events", node.Events)]));
                },
                outputs:
                [
                    ComponentPorts.Metadata<string>("Output"),
                    ComponentPorts.Metadata<ComponentEvent>("Events")
                ]),
            new ComponentDescriptor(
                TestNodeTypes.TickingSource,
                _ =>
                {
                    var node = new TickingSourceNode();
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        outputs: [ComponentPorts.Output<string>("Output", node.Output)],
                        addressableEvents: [ComponentPorts.Events("Events", node.Events)]));
                },
                outputs:
                [
                    ComponentPorts.Metadata<string>("Output"),
                    ComponentPorts.Metadata<ComponentEvent>("Events")
                ]),
            new ComponentDescriptor(
                TestNodeTypes.IntSource,
                _ =>
                {
                    var node = new IntSourceNode();
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        outputs: [ComponentPorts.Output<int>("Output", node.Output)],
                        addressableEvents: [ComponentPorts.Events("Events", node.Events)]));
                },
                outputs:
                [
                    ComponentPorts.Metadata<int>("Output"),
                    ComponentPorts.Metadata<ComponentEvent>("Events")
                ]),
            new ComponentDescriptor(
                TestNodeTypes.Uppercase,
                _ =>
                {
                    var node = new UppercaseNode();
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        inputs: [ComponentPorts.Input<string>("Input", node.Input)],
                        outputs: [ComponentPorts.Output<string>("Output", node.Output)],
                        addressableEvents: [ComponentPorts.Events("Events", node.Events)]));
                },
                inputs: [ComponentPorts.Metadata<string>("Input")],
                outputs:
                [
                    ComponentPorts.Metadata<string>("Output"),
                    ComponentPorts.Metadata<ComponentEvent>("Events")
                ]),
            new ComponentDescriptor(
                TestNodeTypes.Sink,
                context =>
                {
                    var collector = (StringCollector?)context.Services.GetService(typeof(StringCollector))
                        ?? new StringCollector();
                    var node = new CollectSinkNode(collector);
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        inputs: [ComponentPorts.Input<string>("Input", node.Input)],
                        addressableEvents: [ComponentPorts.Events("Events", node.Events)]));
                },
                inputs: [ComponentPorts.Metadata<string>("Input")],
                outputs: [ComponentPorts.Metadata<ComponentEvent>("Events")]),
            new ComponentDescriptor(
                TestNodeTypes.TrackedSource,
                _ =>
                {
                    var node = new TrackedSourceNode(tracker);
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        outputs: [ComponentPorts.Output<string>("Output", node.Output)],
                        addressableEvents: [ComponentPorts.Events("Events", node.Events)]));
                },
                outputs:
                [
                    ComponentPorts.Metadata<string>("Output"),
                    ComponentPorts.Metadata<ComponentEvent>("Events")
                ]),
            new ComponentDescriptor(
                TestNodeTypes.Failing,
                _ => throw new InvalidOperationException("factory failed"))
        ]);
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
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(message), cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class TickingSourceNode : FlowSource<string>
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await EmitAsync(FlowMessage.Create("tick"), cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}

internal sealed class IntSourceNode : FlowSource<int>
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
        => await EmitAsync(FlowMessage.Create(1), cancellationToken).ConfigureAwait(false);
}

internal sealed class TrackedSourceNode(BuildTracker tracker) : FlowSource<string>
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
        => await EmitAsync(FlowMessage.Create("tracked"), cancellationToken).ConfigureAwait(false);

    protected override ValueTask OnDisposeAsync()
    {
        tracker.MarkDisposed();
        return ValueTask.CompletedTask;
    }
}

internal sealed class UppercaseNode : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
        => await EmitAsync(message.With(message.Value.ToUpperInvariant()), Stopping)
            .ConfigureAwait(false);
}

internal sealed class CollectSinkNode(StringCollector collector) : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        collector.Add(message.Value);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}
