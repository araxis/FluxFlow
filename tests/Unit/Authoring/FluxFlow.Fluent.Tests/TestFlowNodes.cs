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

/// <summary>Emits "tick" every 10ms until stopped — an unbounded source, for stop tests.</summary>
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

/// <summary>Uppercases each payload, preserving correlation.</summary>
internal sealed class UppercaseNode : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
        => await EmitAsync(message.With(FlowValue.From(message.Value!.Deserialize<string>()!.ToUpperInvariant())), Stopping)
            .ConfigureAwait(false);
}

/// <summary>Records every payload and re-emits it unchanged, so it works mid-chain or as a sink.</summary>
internal sealed class CollectSinkNode(StringCollector collector) : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        collector.Add(message.Value!.Deserialize<string>()!);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}

/// <summary>Turns an int into "n=&lt;value&gt;" — used to prove type-changing chains compile and run.</summary>
internal sealed class IntToLabelNode : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
        => await EmitAsync(message.With(FlowValue.From($"n={message.Value!.Deserialize<int>()}")), Stopping).ConfigureAwait(false);
}

/// <summary>Emits 0, 1, ..., count-1 then completes.</summary>
internal sealed class IntSourceNode(int count) : FlowSource
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        for (var value = 0; value < count; value++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(FlowValue.From(value)), cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Routes locally materialized integers through two declared canonical ports.</summary>
internal sealed class EvenOddRouter : FlowNode
{
    private readonly FlowOutput<FlowMessage> _even;
    private readonly FlowOutput<FlowMessage> _odd;

    public EvenOddRouter()
    {
        _even = AddOutput<FlowMessage>();
        _odd = AddOutput<FlowMessage>();
        Ports = new FlowPorts(
            new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = Input },
            new Dictionary<string, ISourceBlock<FlowMessage>>
            {
                ["Output"] = Output, ["Even"] = Even, ["Odd"] = Odd
            }, Events);
    }

    public override FlowPorts Ports { get; }

    public ISourceBlock<FlowMessage> Even => _even;

    public ISourceBlock<FlowMessage> Odd => _odd;

    protected override async Task ProcessAsync(FlowMessage message)
    {
        var port = message.Value!.Deserialize<int>() % 2 == 0 ? _even : _odd;
        await EmitAsync(port, message, Stopping).ConfigureAwait(false);
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

/// <summary>Appends "!" to each payload.</summary>
internal sealed class ExclaimNode : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
        => await EmitAsync(message.With(FlowValue.From(message.Value!.Deserialize<string>() + "!")), Stopping).ConfigureAwait(false);
}
