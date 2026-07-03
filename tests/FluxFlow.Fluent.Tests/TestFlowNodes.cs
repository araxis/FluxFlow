using FluxFlow.Nodes;

namespace FluxFlow.Fluent.Tests;

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

/// <summary>Emits a fixed set of messages in order, then completes.</summary>
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

/// <summary>Emits "tick" every 10ms until stopped — an unbounded source, for stop tests.</summary>
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

/// <summary>Uppercases each payload, preserving correlation.</summary>
internal sealed class UppercaseNode : FlowNode<string, string>
{
    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        Emit(message.With(message.Payload.ToUpperInvariant()));
        return Task.CompletedTask;
    }
}

/// <summary>Records every payload and re-emits it unchanged, so it works mid-chain or as a sink.</summary>
internal sealed class CollectSinkNode(StringCollector collector) : FlowNode<string, string>
{
    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        collector.Add(message.Payload);
        Emit(message);
        return Task.CompletedTask;
    }
}

/// <summary>Turns an int into "n=&lt;value&gt;" — used to prove type-changing chains compile and run.</summary>
internal sealed class IntToLabelNode : FlowNode<int, string>
{
    protected override Task ProcessAsync(FlowMessage<int> message)
    {
        Emit(message.With($"n={message.Payload}"));
        return Task.CompletedTask;
    }
}

/// <summary>Emits 0, 1, ..., count-1 then completes.</summary>
internal sealed class IntSourceNode(int count) : FlowSource<int>
{
    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        for (var value = 0; value < count; value++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Emit(FlowMessage.Create(value));
        }

        return Task.CompletedTask;
    }
}
