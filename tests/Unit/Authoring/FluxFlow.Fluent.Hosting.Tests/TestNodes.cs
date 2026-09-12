using FluxFlow.Data;
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

internal sealed class StringSourceNode(IReadOnlyList<string> messages) : FlowSource
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(FlowValue.From(message)), cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class TickingSourceNode : FlowSource
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await EmitAsync(FlowMessage.Create(FlowValue.From("tick")), cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}

internal sealed class UppercaseNode : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
        => await EmitAsync(message.With(FlowValue.From(message.Value!.Deserialize<string>()!.ToUpperInvariant())), Stopping)
            .ConfigureAwait(false);
}

internal sealed class CollectSinkNode(StringCollector collector) : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        collector.Add(message.Value!.Deserialize<string>()!);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}
