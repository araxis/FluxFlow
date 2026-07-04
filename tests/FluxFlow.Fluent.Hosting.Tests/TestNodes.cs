using FluxFlow.Nodes;

namespace FluxFlow.Fluent.Hosting.Tests;

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
