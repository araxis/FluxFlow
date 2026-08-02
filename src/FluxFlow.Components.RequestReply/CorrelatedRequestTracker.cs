using System.Diagnostics.CodeAnalysis;
using FluxFlow.Coordination;
using FluxFlow.Nodes;

namespace FluxFlow.Components.RequestReply;

/// <summary>
/// Compatibility adapter that tracks requests by <see cref="CorrelationId"/>.
/// New workflow coordination should normally select <see cref="TraceId"/> as
/// the key for <see cref="PendingExchangeCoordinator{TKey,TContext,TOutcome}"/>.
/// </summary>
public sealed class CorrelatedRequestTracker<TContext, TResponse> : IAsyncDisposable
{
    private readonly Func<CorrelationId, TContext, FlowMessage<TResponse>, CancellationToken, ValueTask> _completeAsync;
    private readonly Func<CorrelationId, TContext, Exception, CancellationToken, ValueTask> _failAsync;
    private readonly CorrelatedRequestTrackerOptions _options;
    private readonly PendingExchangeCoordinator<CorrelationId, TContext, FlowMessage<TResponse>> _coordinator;
    private readonly object _observationGate = new();
    private readonly HashSet<Task> _observations = [];
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
        _coordinator = new PendingExchangeCoordinator<CorrelationId, TContext, FlowMessage<TResponse>>(
            new PendingExchangeCoordinatorOptions
            {
                DefaultTimeout = _options.Timeout,
                MaxPending = _options.MaxPending,
                SettledKeyCapacity = Math.Max(_options.MaxPending, 4096)
            },
            clock);
    }

    public int PendingCount => _coordinator.PendingCount;

    public CorrelatedRequestStartResult TryAdd(CorrelationId correlationId, TContext context)
    {
        ValidateCorrelationId(correlationId);
        ArgumentNullException.ThrowIfNull(context);

        if (Volatile.Read(ref _disposed) != 0)
            return CorrelatedRequestStartResult.Stopped;

        var started = _coordinator.TryStart(correlationId, context);
        if (started.IsAccepted)
            TrackObservation(ObserveTimeoutAsync(started.Completion!));

        return started.Status switch
        {
            PendingExchangeStartStatus.Accepted => CorrelatedRequestStartResult.Accepted,
            PendingExchangeStartStatus.Duplicate => CorrelatedRequestStartResult.DuplicateCorrelationId,
            PendingExchangeStartStatus.CapacityReached => CorrelatedRequestStartResult.CapacityReached,
            PendingExchangeStartStatus.Stopped => CorrelatedRequestStartResult.Stopped,
            _ => throw new InvalidOperationException($"Unsupported pending exchange start status '{started.Status}'.")
        };
    }

    public async ValueTask<bool> TryCompleteAsync(
        FlowMessage<TResponse> response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (Volatile.Read(ref _disposed) != 0)
            return false;

        if (response.CorrelationId is not { } correlationId)
            return false;

        var feedback = _coordinator.TryResolve(correlationId, response);
        if (!feedback.IsResolved)
            return false;

        var completed = feedback.Completion!;
        await _completeAsync(completed.Key, completed.Context, response, cancellationToken)
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
        if (Volatile.Read(ref _disposed) != 0)
            return false;

        var feedback = _coordinator.TryFault(correlationId, error);
        if (!feedback.IsResolved)
            return false;

        var completed = feedback.Completion!;
        await _failAsync(completed.Key, completed.Context, error, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public bool TryRemove(
        CorrelationId correlationId,
        [MaybeNullWhen(false)] out TContext context)
    {
        ValidateCorrelationId(correlationId);
        if (Volatile.Read(ref _disposed) != 0)
        {
            context = default;
            return false;
        }

        var feedback = _coordinator.TryCancel(correlationId);
        if (!feedback.IsResolved)
        {
            context = default;
            return false;
        }

        context = feedback.Completion!.Context;
        return true;
    }

    public ValueTask FailAllAsync(
        Exception error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);
        return FailCompletionsAsync(_coordinator.FaultAll(error), error, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var error = new OperationCanceledException("The correlated request tracker was disposed.");
        var stopped = _coordinator.Stop(error);
        await FailCompletionsAsync(stopped, error, CancellationToken.None).ConfigureAwait(false);
        await AwaitObservationsAsync().ConfigureAwait(false);
        await _coordinator.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ObserveTimeoutAsync(
        Task<PendingExchangeCompletion<CorrelationId, TContext, FlowMessage<TResponse>>> completionTask)
    {
        var completion = await completionTask.ConfigureAwait(false);
        if (completion.Kind != PendingExchangeCompletionKind.TimedOut)
            return;

        await SafeFailAsync(
                completion.Key,
                completion.Context,
                new TimeoutException($"No response within {_options.Timeout.TotalMilliseconds:0} ms."),
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private void TrackObservation(Task observation)
    {
        lock (_observationGate)
            _observations.Add(observation);

        _ = observation.ContinueWith(
            completed =>
            {
                lock (_observationGate)
                    _observations.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async ValueTask AwaitObservationsAsync()
    {
        Task[] observations;
        lock (_observationGate)
            observations = _observations.ToArray();

        if (observations.Length > 0)
            await Task.WhenAll(observations).ConfigureAwait(false);
    }

    private async ValueTask FailCompletionsAsync(
        IReadOnlyList<PendingExchangeCompletion<CorrelationId, TContext, FlowMessage<TResponse>>> completions,
        Exception error,
        CancellationToken cancellationToken)
    {
        foreach (var completion in completions)
        {
            await SafeFailAsync(completion.Key, completion.Context, error, cancellationToken)
                .ConfigureAwait(false);
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
            // The caller may already be gone; cleanup must still settle every request.
        }
    }

    private static void ValidateOptions(CorrelatedRequestTrackerOptions options)
    {
        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.Timeout, "Timeout must be greater than zero.");
        if (options.SweepInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.SweepInterval, "Sweep interval must be greater than zero.");
        if (options.MaxPending <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxPending, "Maximum pending count must be greater than zero.");
    }

    private static void ValidateCorrelationId(CorrelationId correlationId)
    {
        if (correlationId.IsEmpty)
            throw new ArgumentException("Correlation id must not be empty.", nameof(correlationId));
    }
}
