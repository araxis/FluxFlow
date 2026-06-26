using FluxFlow.Nodes;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace FluxFlow.Components.RequestReply;

/// <summary>
/// Tracks requests that are waiting for a correlated response. Transport nodes own
/// how requests are emitted and acknowledged; this type owns only correlation,
/// duplicate detection, timeout, and cleanup.
/// </summary>
public sealed class CorrelatedRequestTracker<TContext, TResponse> : IAsyncDisposable
{
    private readonly ConcurrentDictionary<CorrelationId, PendingRequest> _pending = new();
    private readonly Func<CorrelationId, TContext, FlowMessage<TResponse>, CancellationToken, ValueTask> _completeAsync;
    private readonly Func<CorrelationId, TContext, Exception, CancellationToken, ValueTask> _failAsync;
    private readonly CorrelatedRequestTrackerOptions _options;
    private readonly TimeProvider _clock;
    private readonly ITimer _sweep;
    private int _disposed;

    public CorrelatedRequestTracker(
        Func<CorrelationId, TContext, FlowMessage<TResponse>, CancellationToken, ValueTask> completeAsync,
        Func<CorrelationId, TContext, Exception, CancellationToken, ValueTask> failAsync,
        CorrelatedRequestTrackerOptions? options = null,
        TimeProvider? clock = null)
    {
        _completeAsync = completeAsync ?? throw new ArgumentNullException(nameof(completeAsync));
        _failAsync = failAsync ?? throw new ArgumentNullException(nameof(failAsync));
        _options = options ?? new CorrelatedRequestTrackerOptions();
        ValidateOptions(_options);
        _clock = clock ?? TimeProvider.System;
        _sweep = _clock.CreateTimer(
            _ => Sweep(),
            null,
            _options.SweepInterval,
            _options.SweepInterval);
    }

    public int PendingCount => _pending.Count;

    public CorrelatedRequestStartResult TryAdd(CorrelationId correlationId, TContext context)
    {
        ValidateCorrelationId(correlationId);
        ArgumentNullException.ThrowIfNull(context);

        if (Volatile.Read(ref _disposed) != 0)
        {
            return CorrelatedRequestStartResult.Stopped;
        }

        var pending = new PendingRequest(context, _clock.GetUtcNow() + _options.Timeout);
        return _pending.TryAdd(correlationId, pending)
            ? CorrelatedRequestStartResult.Accepted
            : CorrelatedRequestStartResult.DuplicateCorrelationId;
    }

    public async ValueTask<bool> TryCompleteAsync(
        FlowMessage<TResponse> response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (Volatile.Read(ref _disposed) != 0
            || !_pending.TryRemove(response.CorrelationId, out var pending))
        {
            return false;
        }

        await _completeAsync(response.CorrelationId, pending.Context, response, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> TryFailAsync(
        CorrelationId correlationId,
        Exception error,
        CancellationToken cancellationToken = default)
    {
        ValidateCorrelationId(correlationId);
        ArgumentNullException.ThrowIfNull(error);

        if (Volatile.Read(ref _disposed) != 0
            || !_pending.TryRemove(correlationId, out var pending))
        {
            return false;
        }

        await _failAsync(correlationId, pending.Context, error, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public bool TryRemove(
        CorrelationId correlationId,
        [MaybeNullWhen(false)] out TContext context)
    {
        ValidateCorrelationId(correlationId);
        if (Volatile.Read(ref _disposed) != 0
            || !_pending.TryRemove(correlationId, out var pending))
        {
            context = default;
            return false;
        }

        context = pending.Context;
        return true;
    }

    public async ValueTask FailAllAsync(
        Exception error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);

        foreach (var pair in _pending.ToArray())
        {
            if (_pending.TryRemove(pair.Key, out var pending))
            {
                await SafeFailAsync(pair.Key, pending.Context, error, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _sweep.Dispose();
        await FailAllAsync(new OperationCanceledException(
                "The correlated request tracker was disposed."))
            .ConfigureAwait(false);
    }

    private void Sweep()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        foreach (var pair in _pending)
        {
            if (pair.Value.Deadline > now
                || !_pending.TryRemove(pair.Key, out var pending))
            {
                continue;
            }

            _ = SafeFailAsync(
                pair.Key,
                pending.Context,
                new TimeoutException(
                    $"No response within {_options.Timeout.TotalMilliseconds:0} ms."),
                CancellationToken.None);
        }
    }

    private async ValueTask SafeFailAsync(
        CorrelationId correlationId,
        TContext context,
        Exception error,
        CancellationToken cancellationToken)
    {
        try
        {
            await _failAsync(correlationId, context, error, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // The caller may already be gone; cleanup must not fault timer/dispose paths.
        }
    }

    private static void ValidateOptions(CorrelatedRequestTrackerOptions options)
    {
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Timeout,
                "Timeout must be greater than zero.");
        }

        if (options.SweepInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.SweepInterval,
                "Sweep interval must be greater than zero.");
        }
    }

    private static void ValidateCorrelationId(CorrelationId correlationId)
    {
        if (correlationId.IsEmpty)
        {
            throw new ArgumentException(
                "Correlation id must not be empty.",
                nameof(correlationId));
        }
    }

    private sealed record PendingRequest(TContext Context, DateTimeOffset Deadline);
}
