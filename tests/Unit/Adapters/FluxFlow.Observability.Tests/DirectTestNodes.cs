using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Observability.Tests.Direct;

/// <summary>Thread-safe collector of payloads a sink node received.</summary>
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

internal sealed class ErrorCollector
{
    private readonly List<FlowError> _items = [];

    public IReadOnlyList<FlowError> Items
    {
        get
        {
            lock (_items)
                return _items.ToArray();
        }
    }

    public void Add(FlowError error)
    {
        lock (_items)
            _items.Add(error);
    }
}

/// <summary>Emits a fixed set of messages in order, then completes.</summary>
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



/// <summary>Records every payload and re-emits it unchanged, so it works mid-chain or as a sink.</summary>
internal sealed class CollectSinkNode(StringCollector collector) : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        collector.Add(message.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}




/// <summary>Throws for every message; the node's safety net turns the throw into a FlowError.</summary>
internal sealed class FaultingNode(string errorMessage) : FlowNode
{
    protected override Task ProcessAsync(FlowMessage message)
        => throw new InvalidOperationException(errorMessage);
}

internal sealed class CollectErrorSinkNode(ErrorCollector collector) : FlowNode
{
    protected override bool HandlesErrors => true;

    protected override Task ProcessAsync(FlowMessage message)
    {
        if (message.Error is not null)
            collector.Add(message.Error);
        return Task.CompletedTask;
    }
}

/// <summary>Emits a named event for each message, then passes the message through unchanged.</summary>
internal sealed class EventNode(string eventName) : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        EmitEvent(new FlowEvent { Name = eventName, CorrelationId = message.CorrelationId });
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}


