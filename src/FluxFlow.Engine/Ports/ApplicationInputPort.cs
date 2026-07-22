using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal interface IApplicationInputPort
{
    ApplicationAddress Address { get; }

    Type PayloadType { get; }

    ApplicationPortKind Kind { get; }

    Task Completion { get; }

    ApplicationPortStatus GetStatus();

    ValueTask<IApplicationInputRevision> BeginRevisionAsync(CancellationToken cancellationToken);

    void Complete();

    void Abort();
}

internal interface IApplicationInputRevision : IAsyncDisposable
{
    ApplicationAddress Address { get; }

    Type PayloadType { get; }

    IAsyncDisposable Commit(object? target);
}

internal sealed class ApplicationInputPort<T> : IApplicationInputPort
{
    private readonly ApplicationInputPortCore<FlowMessage<T>, ITargetBlock<FlowMessage<T>>> _core;

    public ApplicationInputPort(
        ApplicationAddress address,
        int capacity,
        Action<ApplicationPortRejection> report,
        Action<ApplicationPortActivity> activity)
    {
        Address = address;
        Capacity = capacity;
        _core = new ApplicationInputPortCore<FlowMessage<T>, ITargetBlock<FlowMessage<T>>>(
            address,
            ApplicationPortKind.Message,
            typeof(T),
            capacity,
            "Input port",
            $"target payload type '{typeof(T)}'",
            report,
            activity,
            static message => new ApplicationInputMessageIdentity(
                message.CorrelationId,
                message.TraceId,
                message.MessageId),
            static (message, target, cancellationToken) =>
                new ValueTask<bool>(target.SendAsync(message, cancellationToken)),
            static target => target.Completion,
            CompleteTargetAsync);
    }

    public ApplicationAddress Address { get; }

    public Type PayloadType => typeof(T);

    public ApplicationPortKind Kind => ApplicationPortKind.Message;

    public int Capacity { get; }

    public Task Completion => _core.Completion;

    public PortSendResult TrySend(
        FlowMessage<T> message,
        ApplicationAddress? source = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _core.TrySend(message, source);
    }

    public ApplicationPortStatus GetStatus()
    {
        return _core.GetStatus();
    }

    public async ValueTask<IAsyncDisposable> AttachAsync(
        ITargetBlock<FlowMessage<T>> target,
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
        return new InputRevision(this);
    }

    public void Complete()
    {
        _core.Complete();
    }

    public void Abort()
    {
        _core.Abort();
    }

    private static async Task CompleteTargetAsync(ITargetBlock<FlowMessage<T>> target)
    {
        target.Complete();
        await target.Completion.ConfigureAwait(false);
    }

    private async ValueTask DetachAsync(long generation)
        => await _core.DetachAsync(generation).ConfigureAwait(false);

    private IAsyncDisposable CommitRevision(object? target)
        => new InputAttachment(this, _core.CommitRevision(target));

    private void EndRevision() => _core.EndRevision();

    private sealed class InputAttachment(
        ApplicationInputPort<T> owner,
        long generation) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                await owner.DetachAsync(generation).ConfigureAwait(false);
        }
    }

    private sealed class InputRevision(ApplicationInputPort<T> owner) :
        IApplicationInputRevision
    {
        private int _committed;
        private int _disposed;

        public ApplicationAddress Address => owner.Address;

        public Type PayloadType => owner.PayloadType;

        public IAsyncDisposable Commit(object? target)
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(InputRevision));
            if (Interlocked.Exchange(ref _committed, 1) != 0)
                throw new InvalidOperationException($"Input port '{Address}' revision was already committed.");
            return owner.CommitRevision(target);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.EndRevision();
            return ValueTask.CompletedTask;
        }
    }
}
