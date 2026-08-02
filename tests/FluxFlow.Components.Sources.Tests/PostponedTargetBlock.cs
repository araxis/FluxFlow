using System.Threading.Tasks.Dataflow;
using Shouldly;

namespace FluxFlow.Components.Sources.Tests;

internal sealed class PostponedTargetBlock<T> : ITargetBlock<T>
{
    private readonly object _gate = new();
    private readonly Queue<PendingOffer> _pending = [];
    private readonly List<T> _accepted = [];
    private readonly SemaphoreSlim _offered = new(0);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<T> Accepted
    {
        get
        {
            lock (_gate)
            {
                return _accepted.ToArray();
            }
        }
    }

    public Task Completion => _completion.Task;

    public DataflowMessageStatus OfferMessage(
        DataflowMessageHeader messageHeader,
        T messageValue,
        ISourceBlock<T>? source,
        bool consumeToAccept)
    {
        if (Completion.IsCompleted)
        {
            return DataflowMessageStatus.DecliningPermanently;
        }

        if (source is null)
        {
            return DataflowMessageStatus.Declined;
        }

        lock (_gate)
        {
            _pending.Enqueue(new PendingOffer(messageHeader, source));
        }

        _offered.Release();
        return DataflowMessageStatus.Postponed;
    }

    public async Task WaitForOfferAsync(TimeSpan timeout)
    {
        (await _offered.WaitAsync(timeout)).ShouldBeTrue(
            "The reliable output did not offer a message before the test deadline.");
    }

    public void AcceptNext()
    {
        PendingOffer offer;
        lock (_gate)
        {
            offer = _pending.Dequeue();
        }

        var value = offer.Source.ConsumeMessage(
            offer.Header,
            this,
            out var messageConsumed);
        messageConsumed.ShouldBeTrue();
        lock (_gate)
        {
            _accepted.Add(value!);
        }
    }

    public void Complete() => _completion.TrySetResult();

    public void Fault(Exception exception) => _completion.TrySetException(exception);

    private sealed record PendingOffer(
        DataflowMessageHeader Header,
        ISourceBlock<T> Source);
}
