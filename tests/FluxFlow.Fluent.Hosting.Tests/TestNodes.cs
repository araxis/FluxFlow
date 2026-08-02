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
