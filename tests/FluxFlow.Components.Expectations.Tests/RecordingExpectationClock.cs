using FluxFlow.Components.Expectations.Timing;

namespace FluxFlow.Components.Expectations.Tests;

internal sealed class RecordingExpectationClock(DateTimeOffset utcNow) : IExpectationClock
{
    private readonly object _gate = new();
    private readonly Queue<DelayRequest> _delays = [];

    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public int PendingDelayCount
    {
        get
        {
            lock (_gate)
            {
                return _delays.Count;
            }
        }
    }

    public TimeSpan? NextDelay
    {
        get
        {
            lock (_gate)
            {
                return _delays.TryPeek(out var request) ? request.Delay : null;
            }
        }
    }

    public async Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetCanceled(),
            completion);

        lock (_gate)
        {
            _delays.Enqueue(new DelayRequest(delay, completion));
        }

        await completion.Task.ConfigureAwait(false);
    }

    public void CompleteNextDelay()
    {
        DelayRequest request;
        lock (_gate)
        {
            request = _delays.Dequeue();
        }

        request.Completion.TrySetResult();
    }

    private sealed record DelayRequest(
        TimeSpan Delay,
        TaskCompletionSource Completion);
}
