using System.Runtime.ExceptionServices;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Core.Coordination;

/// <summary>Matches component requests with their first reply, with bounded capacity and cancellation.</summary>
public sealed class FlowRequestReply : IAsyncDisposable
{
    private readonly PendingRequests _pending;

    public FlowRequestReply(int capacity = 1024, TimeProvider? timeProvider = null)
        => _pending = new(capacity, timeProvider);

    public int PendingCount => _pending.Count;

    /// <summary>The deadline covers both admission and reply waiting. The sender receives a fresh request correlation.</summary>
    public async Task<FlowMessage> RequestAsync(string replyAddress, FlowMessage request,
        Func<FlowMessage, CancellationToken, ValueTask> send, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replyAddress);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(send);
        cancellationToken.ThrowIfCancellationRequested();
        var key = (Correlation: CorrelationId.New(), Address: replyAddress);
        var pending = _pending.Add(key, timeout);

        using var activity = FlowTracing.StartRequest(request, replyAddress);
        try
        {
            using var admissionStopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var cancellation = cancellationToken.Register(() =>
                _pending.Complete(key, error: new OperationCanceledException(cancellationToken)));
            var headers = activity.CaptureHeaders(request.Headers);
            var envelope = new FlowMessage(request.Value, request.MessageId, request.TraceId, request.Timestamp,
                key.Correlation, request.CausationId, headers);
            var admission = AdmitAsync(send, envelope, key, admissionStopped.Token);
            try
            {
                var completion = await pending.ConfigureAwait(false);
                if (completion.Error is { } error) ExceptionDispatchInfo.Capture(error).Throw();
                activity.Outcome("received");
                return completion.Reply!;
            }
            finally
            {
                // Expiry also cancels a postponed native SendAsync. Do not leave detached send tasks.
                admissionStopped.Cancel();
                await admission.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            activity.Canceled();
            throw;
        }
        catch (Exception error)
        {
            activity.Failed(error);
            activity.Outcome(error is TimeoutException ? "timed_out" : "failed");
            throw;
        }
    }

    public bool Reply(string replyAddress, FlowMessage message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replyAddress);
        ArgumentNullException.ThrowIfNull(message);
        return message.CorrelationId is { } correlation
            && _pending.Complete((correlation, replyAddress), message);
    }

    public void Stop(Exception? error = null) => _pending.Stop(error);
    public ValueTask DisposeAsync() => _pending.DisposeAsync();

    private async Task AdmitAsync(Func<FlowMessage, CancellationToken, ValueTask> send,
        FlowMessage message, (CorrelationId Correlation, string Address) key, CancellationToken cancellationToken)
    {
        try
        {
            await send(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) { _pending.Complete(key, error: error); }
    }
}
