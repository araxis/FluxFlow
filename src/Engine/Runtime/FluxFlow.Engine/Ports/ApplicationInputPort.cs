using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal interface IApplicationInputPort
{
    ApplicationAddress Address { get; }

    Type PayloadType { get; }

    ApplicationPortKind Kind { get; }

    Task Completion { get; }

    ApplicationPortStatus GetStatus();

    PortSendResult TrySend(FlowMessage message, ApplicationAddress? source = null);

    ValueTask<PortSendResult> SendAsync(FlowMessage message,
        ApplicationAddress? source, CancellationToken cancellationToken);

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

internal interface IApplicationInputAttachment : IAsyncDisposable
{
    ApplicationAddress Address { get; }

    ValueTask DrainAsync(CancellationToken cancellationToken);
}

internal sealed class ApplicationInputPort : IApplicationInputPort
{
    private readonly ApplicationInputPortCore _core;

    public ApplicationInputPort(
        ApplicationAddress address,
        int capacity,
        Action<ApplicationPortRejection> report,
        Action<ApplicationPortActivity> activity,
        ApplicationPortKind kind = ApplicationPortKind.Message)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Address = address;
        Kind = kind;
        Capacity = capacity;
        _core = new ApplicationInputPortCore(
            address,
            Kind,
            PayloadType,
            capacity,
            "Input port",
            $"target payload type '{typeof(global::FluxFlow.Data.FlowValue)}'",
            report,
            activity);
    }

    public ApplicationAddress Address { get; }

    // Control metadata is payload-agnostic; its physical transport is still FlowMessage.
    public Type PayloadType => Kind == ApplicationPortKind.Signal
        ? typeof(object) : typeof(global::FluxFlow.Data.FlowValue);

    public ApplicationPortKind Kind { get; }

    public int Capacity { get; }

    public Task Completion => _core.Completion;

    public PortSendResult TrySend(
        FlowMessage message,
        ApplicationAddress? source = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _core.TrySend(message, source);
    }

    public ApplicationPortStatus GetStatus()
    {
        return _core.GetStatus();
    }

    public ValueTask<PortSendResult> SendAsync(FlowMessage message,
        ApplicationAddress? source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _core.SendAsync(message, source, cancellationToken);
    }

    public async ValueTask<IAsyncDisposable> AttachAsync(
        ITargetBlock<FlowMessage> target,
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
            "Input port",
            target => new ApplicationInputAttachmentLifetime(
                Address,
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
}
