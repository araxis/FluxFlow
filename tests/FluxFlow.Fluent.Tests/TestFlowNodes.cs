using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
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

/// <summary>Emits "tick" every 10ms until stopped — an unbounded source, for stop tests.</summary>
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

/// <summary>Uppercases each payload, preserving correlation.</summary>
internal sealed class UppercaseNode : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
        => await EmitAsync(message.With(message.Value.ToUpperInvariant()), Stopping)
            .ConfigureAwait(false);
}

/// <summary>Records every payload and re-emits it unchanged, so it works mid-chain or as a sink.</summary>
internal sealed class CollectSinkNode(StringCollector collector) : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        collector.Add(message.Value);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}

/// <summary>Turns an int into "n=&lt;value&gt;" — used to prove type-changing chains compile and run.</summary>
internal sealed class IntToLabelNode : FlowNode<int, string>
{
    protected override async Task ProcessAsync(FlowMessage<int> message)
        => await EmitAsync(message.With($"n={message.Value}"), Stopping).ConfigureAwait(false);
}

/// <summary>Emits 0, 1, ..., count-1 then completes.</summary>
internal sealed class IntSourceNode(int count) : FlowSource<int>
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        for (var value = 0; value < count; value++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(value), cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Routes each int to one of two typed output ports by parity — used to exercise branching.</summary>
internal sealed class EvenOddRouter : FlowNode<int, int>
{
    private readonly FlowOutput<FlowMessage<int>> _even;
    private readonly FlowOutput<FlowMessage<int>> _odd;

    public EvenOddRouter()
    {
        _even = AddOutput<FlowMessage<int>>();
        _odd = AddOutput<FlowMessage<int>>();
    }

    public ISourceBlock<FlowMessage<int>> Even => _even;

    public ISourceBlock<FlowMessage<int>> Odd => _odd;

    protected override async Task ProcessAsync(FlowMessage<int> message)
    {
        var port = message.Value % 2 == 0 ? _even : _odd;
        await EmitAsync(port, message, Stopping).ConfigureAwait(false);
    }
}

/// <summary>Throws for every message; the node's safety net turns the throw into a FlowError.</summary>
internal sealed class FaultingNode(string errorMessage) : FlowNode<string, string>
{
    protected override Task ProcessAsync(FlowMessage<string> message)
        => throw new InvalidOperationException(errorMessage);
}

internal sealed class CollectErrorSinkNode(ErrorCollector collector) : FlowNode<string, string>
{
    protected override bool HandlesErrors => true;

    protected override Task ProcessAsync(FlowMessage<string> message)
    {
        if (message.Error is not null)
            collector.Add(message.Error);
        return Task.CompletedTask;
    }
}

/// <summary>Emits a named event for each message, then passes the message through unchanged.</summary>
internal sealed class EventNode(string eventName) : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        EmitEvent(new FlowEvent { Name = eventName, CorrelationId = message.CorrelationId });
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}

/// <summary>Appends "!" to each payload.</summary>
internal sealed class ExclaimNode : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
        => await EmitAsync(message.With(message.Value + "!"), Stopping).ConfigureAwait(false);
}
