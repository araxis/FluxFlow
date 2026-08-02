using System.Collections.Concurrent;

namespace FluxFlow.Engine.DurableOutput.Tests;

internal sealed class RecordingDurableOutputStore : IDurableOutputStore
{
    private readonly ConcurrentQueue<DurableOutputEnvelope> _envelopes = new();
    private readonly ConcurrentQueue<CancellationToken> _cancellationTokens = new();

    internal IReadOnlyList<DurableOutputEnvelope> Envelopes => _envelopes.ToArray();

    internal IReadOnlyList<CancellationToken> CancellationTokens => _cancellationTokens.ToArray();

    internal DurableOutputEnqueueStatus Status { get; set; } = DurableOutputEnqueueStatus.Enqueued;

    internal DurableOutputKey? ResultKey { get; set; }

    internal Exception? EnqueueException { get; set; }

    internal bool ReturnNull { get; set; }

    public ValueTask<DurableOutputEnqueueResult> EnqueueAsync(
        DurableOutputEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _envelopes.Enqueue(envelope);
        _cancellationTokens.Enqueue(cancellationToken);
        if (EnqueueException is not null)
            return ValueTask.FromException<DurableOutputEnqueueResult>(EnqueueException);
        if (ReturnNull)
            return ValueTask.FromResult<DurableOutputEnqueueResult>(null!);

        return ValueTask.FromResult(new DurableOutputEnqueueResult(
            ResultKey ?? envelope.Key,
            Status));
    }
}
