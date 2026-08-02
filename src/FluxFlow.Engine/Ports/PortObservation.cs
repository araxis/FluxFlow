using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

public sealed class PortObservation<T> : IAsyncDisposable
{
    private readonly BufferBlock<FlowMessage<T>> _messages;
    private readonly Action<PortObservation<T>> _remove;
    private int _disposed;

    internal PortObservation(
        ApplicationAddress port,
        int capacity,
        Action<PortObservation<T>> remove)
    {
        Port = port;
        Capacity = capacity;
        _remove = remove;
        _messages = new BufferBlock<FlowMessage<T>>(new DataflowBlockOptions
        {
            BoundedCapacity = capacity
        });
    }

    public ApplicationAddress Port { get; }

    public int Capacity { get; }

    public ISourceBlock<FlowMessage<T>> Messages => _messages;

    public Task Completion => _messages.Completion;

    internal bool IsActive => Volatile.Read(ref _disposed) == 0;

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

    internal bool TryPost(FlowMessage<T> message)
        => IsActive && _messages.Post(message);

    internal void Complete()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _remove(this);
            _messages.Complete();
        }
    }

    internal void Fault(Exception exception)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _remove(this);
            ((IDataflowBlock)_messages).Fault(exception);
        }
    }
}
