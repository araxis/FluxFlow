using FluxFlow.Core.Messaging;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Core.Nodes;

/// <summary>Optional emission helpers and lifecycle forwarding. Components construct their own blocks and lifetime.</summary>
public abstract class FlowNode : IFlowNode
{
    public BroadcastBlock<FlowMessage> Output { get; protected init; } = null!;
    public BroadcastBlock<FlowMessage> Events { get; protected init; } = null!;
    protected FlowLifetime Lifetime { get; init; } = null!;
    protected CancellationToken Stopping => Lifetime.Stopping;
    public IEnumerable<ITargetBlock<FlowMessage>> TerminalInputs => Lifetime.TerminalInputs;
    public Task Completion => Lifetime.Completion;

    /// <summary>Publishes immediately. Direct sends are not reordered by EnsureOrdered.</summary>
    protected ValueTask EmitAsync(FlowMessage message, CancellationToken cancellationToken = default)
        => Output.EmitAsync(message, cancellationToken, "The node data output is no longer accepting messages.");

    protected static ValueTask EmitAsync<T>(BroadcastBlock<T> output, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.EmitAsync(value, cancellationToken, "The node data output is no longer accepting messages.");
    }

    protected ValueTask EmitErrorAsync(FlowError error, CancellationToken cancellationToken = default)
        => EmitAsync(FlowMessage.Create(error), cancellationToken);
    protected bool EmitEvent(FlowEvent @event) => Events.Post(@event);
    protected bool EmitEvent(FlowMessage @event) => Events.Post(@event);

    public void Complete() => Lifetime.Complete();
    public void Fault(Exception exception) => Lifetime.Fault(exception);
    public ValueTask DisposeAsync() => Lifetime.DisposeAsync(OnDisposeAsync);
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;
}
