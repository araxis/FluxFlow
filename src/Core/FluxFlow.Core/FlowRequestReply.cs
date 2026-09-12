using System.Runtime.ExceptionServices;
using FluxFlow.Coordination;
using FluxFlow.Nodes;

namespace FluxFlow.Core;

/// <summary>Bounded first-reply exchanges shared by application adapters over canonical messages.</summary>
public sealed class FlowRequestReply : IAsyncDisposable
{
    private readonly PendingExchangeCoordinator<ExchangeKey, string, FlowMessage> _pending;

    public FlowRequestReply(int capacity = 1024, TimeProvider? timeProvider = null)
        => _pending = new(new PendingExchangeCoordinatorOptions { MaxPending = capacity }, timeProvider);

    public int PendingCount => _pending.PendingCount;

    /// <summary>The deadline covers both admission and reply waiting. The sender receives a fresh exchange correlation.</summary>
    public async Task<FlowMessage> RequestAsync(string replyAddress, FlowMessage request,
        Func<FlowMessage, CancellationToken, ValueTask> send, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replyAddress);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(send);
        cancellationToken.ThrowIfCancellationRequested();
        var key = new ExchangeKey(CorrelationId.New(), replyAddress);
        var start = _pending.TryStart(key, replyAddress, timeout);
        if (start.Status == PendingExchangeStartStatus.Stopped)
            throw new OperationCanceledException("The application stopped before the request was admitted.");
        if (!start.IsAccepted)
            throw new InvalidOperationException($"Request was not accepted: {start.Status}.");

        using var activity = FlowTracing.StartRequest(request, replyAddress);
        try
        {
            using var admissionStopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var cancellation = cancellationToken.Register(() =>
                _pending.TryCancel(key, new OperationCanceledException(cancellationToken)));
            var headers = activity.CaptureHeaders(request.Headers);
            var envelope = request.IsError
                ? FlowMessage.RestoreError(request.Error!, request.MessageId, request.TraceId, request.Timestamp,
                    key.Correlation, request.CausationId, headers)
                : FlowMessage.Restore(request.Value!, request.MessageId, request.TraceId, request.Timestamp,
                    key.Correlation, request.CausationId, headers);
            var admission = AdmitAsync(send, envelope, key, admissionStopped.Token);
            try
            {
                var completion = await start.Completion!.ConfigureAwait(false);
                if (completion.Kind == PendingExchangeCompletionKind.Resolved)
                {
                    activity.Outcome("received");
                    return completion.Outcome!;
                }
                if (completion.Error is { } error) ExceptionDispatchInfo.Capture(error).Throw();
                if (completion.Kind == PendingExchangeCompletionKind.TimedOut)
                    throw new TimeoutException("The request deadline expired during admission or reply waiting.");
                throw new OperationCanceledException("The application stopped before the request completed.");
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
            && _pending.TryResolve(new ExchangeKey(correlation, replyAddress), message).IsResolved;
    }

    public void Stop(Exception? error = null) => _pending.Stop(error);
    public ValueTask DisposeAsync() => _pending.DisposeAsync();

    private async Task AdmitAsync(Func<FlowMessage, CancellationToken, ValueTask> send,
        FlowMessage message, ExchangeKey key, CancellationToken cancellationToken)
    {
        try
        {
            await send(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) { _pending.TryCancel(key, error); }
        catch (Exception error) { _pending.TryFault(key, error); }
    }

    private readonly record struct ExchangeKey(CorrelationId Correlation, string ReplyAddress);
}
