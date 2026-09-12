using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal interface IPortObservation
{
    int Capacity { get; }
    bool IsActive { get; }
    bool TryPost(FlowMessage message);
    void Complete();
    void Fault(Exception exception);
}

public sealed class PortObservation<T> : IAsyncDisposable, IPortObservation
{
    private readonly TransformBlock<FlowMessage, FlowMessage<T>> _messages;
    private readonly Action<IPortObservation> _remove;
    private int _disposed;

    internal PortObservation(
        ApplicationAddress port,
        int capacity,
        Action<IPortObservation> remove)
    {
        Port = port;
        Capacity = capacity;
        _remove = remove;
        _messages = new TransformBlock<FlowMessage, FlowMessage<T>>(Project, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = capacity
        });
    }

    public ApplicationAddress Port { get; }

    public int Capacity { get; }

    public ISourceBlock<FlowMessage<T>> Messages => _messages;

    public Task Completion => _messages.Completion;

    bool IPortObservation.IsActive => Volatile.Read(ref _disposed) == 0;

    public async IAsyncEnumerable<FlowMessage<T>> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await _messages.OutputAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_messages.TryReceive(out var message))
                yield return message;
        }

        await _messages.Completion.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _remove(this);
            _messages.Complete();
        }

        return ValueTask.CompletedTask;
    }

    bool IPortObservation.TryPost(FlowMessage message)
        => Volatile.Read(ref _disposed) == 0 && _messages.Post(message);

    void IPortObservation.Complete() => _ = DisposeAsync();

    void IPortObservation.Fault(Exception exception) => Fault(exception);

    private void Fault(Exception exception)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _remove(this);
            ((IDataflowBlock)_messages).Fault(exception);
        }
    }

    private FlowMessage<T> Project(FlowMessage message)
    {
        try
        {
            return ApplicationMessageProjection.ToTyped<T>(message);
        }
        catch (Exception exception)
        {
            Fault(exception);
            throw;
        }
    }
}
