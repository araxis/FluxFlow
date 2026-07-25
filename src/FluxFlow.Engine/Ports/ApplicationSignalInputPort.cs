using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal interface IApplicationSignalInputPort : IApplicationInputPort, IFlowSignalTarget
{
    PortSendResult TrySend<T>(
        FlowMessage<T> message,
        ApplicationAddress? source = null);

    ValueTask<IAsyncDisposable> AttachAsync(
        IFlowSignalTarget target,
        CancellationToken cancellationToken);
}

internal sealed class ApplicationSignalInputPort : IApplicationSignalInputPort
{
    private readonly ApplicationInputPortCore<ISignalEnvelope, IFlowSignalTarget> _core;

    public ApplicationSignalInputPort(
        ApplicationAddress address,
        int capacity,
        Action<ApplicationPortRejection> report,
        Action<ApplicationPortActivity> activity)
    {
        Address = address;
        Capacity = capacity;
        _core = new ApplicationInputPortCore<ISignalEnvelope, IFlowSignalTarget>(
            address,
            ApplicationPortKind.Signal,
            typeof(object),
            capacity,
            "Signal input port",
            $"an {nameof(IFlowSignalTarget)} target",
            report,
            activity,
            static message => new ApplicationInputMessageIdentity(
                message.CorrelationId,
                message.TraceId,
                message.MessageId),
            static (message, target, cancellationToken) =>
                message.SendAsync(target, cancellationToken),
            static target => target.Completion,
            static _ => Task.CompletedTask);
    }

    public ApplicationAddress Address { get; }

    public Type PayloadType => typeof(object);

    public ApplicationPortKind Kind => ApplicationPortKind.Signal;

    public int Capacity { get; }

    public Task Completion => _core.Completion;

    public PortSendResult TrySend<T>(
        FlowMessage<T> message,
        ApplicationAddress? source = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _core.TrySend(new SignalEnvelope<T>(message), source);
    }

    public ValueTask<bool> SendAsync<T>(
        FlowMessage<T> signal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(TrySend(signal).IsAccepted);
    }

    public ApplicationPortStatus GetStatus()
    {
        return _core.GetStatus();
    }

    public async ValueTask<IAsyncDisposable> AttachAsync(
        IFlowSignalTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var revision = await BeginRevisionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return revision.Commit(target);
        }
        finally
        {
            await revision.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<IApplicationInputRevision> BeginRevisionAsync(
        CancellationToken cancellationToken)
    {
        await _core.BeginRevisionAsync(cancellationToken).ConfigureAwait(false);
        return new ApplicationInputRevisionLifetime(
            Address,
            PayloadType,
            "Signal input port",
            target => new ApplicationInputAttachmentLifetime(
                _core.CommitRevision(target),
                _core.DrainAsync,
                _core.DetachAsync),
            _core.EndRevision);
    }

    public void Complete()
    {
        _core.Complete();
    }

    public void Abort()
    {
        _core.Abort();
    }

    internal interface ISignalEnvelope
    {
        CorrelationId CorrelationId { get; }

        TraceId TraceId { get; }

        MessageId MessageId { get; }

        ValueTask<bool> SendAsync(
            IFlowSignalTarget target,
            CancellationToken cancellationToken);
    }

    private sealed class SignalEnvelope<T>(FlowMessage<T> message) : ISignalEnvelope
    {
        public CorrelationId CorrelationId => message.CorrelationId;

        public TraceId TraceId => message.TraceId;

        public MessageId MessageId => message.MessageId;

        public ValueTask<bool> SendAsync(
            IFlowSignalTarget target,
            CancellationToken cancellationToken)
            => target.SendAsync(message, cancellationToken);
    }

}
