using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Hosting.Tests;

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
            await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class TickingSourceNode : FlowSource
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("tick", new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}

internal sealed class UppercaseNode : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
        => await EmitAsync(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value!.Json.Deserialize<string>()!.ToUpperInvariant(), new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Stopping)
            .ConfigureAwait(false);
}

internal sealed class CollectSinkNode(StringCollector collector) : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        collector.Add(message.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}
