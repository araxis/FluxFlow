using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.DurableOutput;

internal sealed class DurableOutputDeliveryDispatcher : BackgroundService
{
    private readonly IDurableOutputDeliveryStore _store;
    private readonly IDurableOutputDeliveryHandler _handler;
    private readonly DurableOutputDeliveryOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DurableOutputDeliveryDispatcher> _logger;
    private readonly string _ownerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():n}";

    public DurableOutputDeliveryDispatcher(
        IEnumerable<IDurableOutputDeliveryStore> stores,
        IEnumerable<IDurableOutputDeliveryHandler> handlers,
        DurableOutputDeliveryOptions options,
        TimeProvider clock,
        ILogger<DurableOutputDeliveryDispatcher> logger)
        : this(
            GetRequiredStore(stores),
            GetRequiredHandler(handlers),
            options,
            clock,
            logger)
    {
    }

    internal DurableOutputDeliveryDispatcher(
        IDurableOutputDeliveryStore store,
        IDurableOutputDeliveryHandler handler,
        DurableOutputDeliveryOptions options,
        TimeProvider clock,
        ILogger<DurableOutputDeliveryDispatcher> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Durable output delivery dispatcher {LeaseOwner} started.",
            _ownerId);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (await ProcessOnceAsync(stoppingToken).ConfigureAwait(false))
                        continue;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (DurableOutputDeliveryStoreException exception)
                {
                    _logger.LogError(
                        "Durable output delivery store operation {StoreOperation} failed with {StoreExceptionType}; ownership recovers after lease expiry.",
                        exception.Operation,
                        (exception.InnerException ?? exception).GetType().FullName);
                }

                try
                {
                    await Task.Delay(_options.IdleDelay, _clock, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _logger.LogInformation(
                "Durable output delivery dispatcher {LeaseOwner} stopped.",
                _ownerId);
        }
    }

    internal async ValueTask<bool> ProcessOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var request = new DurableOutputDeliveryLeaseRequest(
            _ownerId,
            now,
            now + _options.LeaseDuration);
        var lease = await InvokeStoreAsync(
                "lease",
                () => _store.TryLeaseAsync(request, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        if (lease is null)
            return false;

        ValidateLease(request, lease);
        DurableOutputInstrumentation.RecordLeaseAcquired();
        _logger.LogDebug(
            "Leased durable output {MessageId} at {Address} for attempt {Attempt} by {LeaseOwner}.",
            lease.Envelope.MessageId,
            lease.Envelope.Address.Value,
            lease.Attempt,
            lease.OwnerId);

        var durationStartedAt = DurableOutputInstrumentation.StartDeliveryDuration(_clock);
        var activity = DurableOutputInstrumentation.StartDeliveryActivity(lease);
        var activityOutcome = "failed";
        try
        {
            bool stillOwnsLease;
            try
            {
                stillOwnsLease = await DeliverWithRenewalAsync(lease, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DurableOutputInstrumentation.RecordHandlerCall("canceled");
                activityOutcome = "canceled";
                throw;
            }
            catch (DurableOutputDeliveryStoreException)
            {
                DurableOutputInstrumentation.RecordHandlerCall("canceled");
                throw;
            }
            catch (Exception exception)
            {
                DurableOutputInstrumentation.RecordHandlerCall("failed");
                if (_options.MaxDeliveryAttempts is { } maximum && lease.Attempt >= maximum)
                {
                    _logger.LogWarning(
                        "Durable output handler {HandlerExceptionType} failed for {MessageId} at {Address} on final attempt {Attempt}; moving the lease to dead letter.",
                        exception.GetType().FullName,
                        lease.Envelope.MessageId,
                        lease.Envelope.Address.Value,
                        lease.Attempt);
                    await DeadLetterAsync(lease, cancellationToken).ConfigureAwait(false);
                    activityOutcome = "dead_letter";
                }
                else
                {
                    _logger.LogWarning(
                        "Durable output handler {HandlerExceptionType} failed for {MessageId} at {Address} on attempt {Attempt}; scheduling retry.",
                        exception.GetType().FullName,
                        lease.Envelope.MessageId,
                        lease.Envelope.Address.Value,
                        lease.Attempt);
                    await RetryAsync(lease, cancellationToken).ConfigureAwait(false);
                    activityOutcome = "retry";
                }

                return true;
            }

            if (!stillOwnsLease)
            {
                DurableOutputInstrumentation.RecordHandlerCall("canceled");
                DurableOutputInstrumentation.RecordDelivery("ownership_lost");
                activityOutcome = "ownership_lost";
                return true;
            }

            DurableOutputInstrumentation.RecordHandlerCall("succeeded");
            await CompleteAsync(lease, cancellationToken).ConfigureAwait(false);
            activityOutcome = "completed";
            return true;
        }
        finally
        {
            DurableOutputInstrumentation.RecordDeliveryDuration(_clock, durationStartedAt);
            DurableOutputInstrumentation.CompleteDeliveryActivity(activity, activityOutcome);
        }
    }

    private async ValueTask<bool> DeliverWithRenewalAsync(
        DurableOutputDeliveryLease lease,
        CancellationToken cancellationToken)
    {
        using var handlerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task handlerTask;
        try
        {
            handlerTask = _handler
                .DeliverAsync(lease.Envelope, handlerCancellation.Token)
                .AsTask();
        }
        catch
        {
            await CancelAndObserveHandlerAsync(
                    lease,
                    handlerCancellation,
                    handlerTask: null)
                .ConfigureAwait(false);
            throw;
        }

        try
        {
            using var renewalTimer = new PeriodicTimer(
                _options.LeaseRenewalInterval,
                _clock);
            while (true)
            {
                if (handlerTask.IsCompleted)
                {
                    await handlerTask.ConfigureAwait(false);
                    return true;
                }

                var renewalTick = renewalTimer
                    .WaitForNextTickAsync(cancellationToken)
                    .AsTask();
                await Task.WhenAny(handlerTask, renewalTick).ConfigureAwait(false);

                if (handlerTask.IsCompleted)
                {
                    renewalTimer.Dispose();
                    await handlerTask.ConfigureAwait(false);
                    return true;
                }

                if (!await renewalTick.ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "The durable output delivery renewal timer stopped unexpectedly.");
                }

                if (handlerTask.IsCompleted)
                {
                    await handlerTask.ConfigureAwait(false);
                    return true;
                }

                var now = _clock.GetUtcNow();
                var renewal = new DurableOutputDeliveryLeaseRenewal(
                    lease.Envelope.Key,
                    lease.LeaseToken,
                    now,
                    now + _options.LeaseDuration);
                var result = await InvokeStoreAsync(
                        "renew-lease",
                        () => _store.RenewLeaseAsync(renewal, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
                ValidateTransitionKey("renew-lease", lease, result);
                DurableOutputInstrumentation.RecordLeaseRenewal(result.IsApplied);
                if (result.IsApplied)
                {
                    _logger.LogDebug(
                        "Renewed durable output delivery lease for {MessageId} from {Address} on attempt {Attempt}.",
                        lease.Envelope.MessageId,
                        lease.Envelope.Address.Value,
                        lease.Attempt);
                    continue;
                }

                _logger.LogDebug(
                    "Stopped durable output delivery for {MessageId} from {Address} because lease renewal returned {TransitionStatus}.",
                    lease.Envelope.MessageId,
                    lease.Envelope.Address.Value,
                    result.Status);
                await CancelAndObserveHandlerAsync(
                        lease,
                        handlerCancellation,
                        handlerTask)
                    .ConfigureAwait(false);
                return false;
            }
        }
        catch
        {
            await CancelAndObserveHandlerAsync(
                    lease,
                    handlerCancellation,
                    handlerTask)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask CancelAndObserveHandlerAsync(
        DurableOutputDeliveryLease lease,
        CancellationTokenSource handlerCancellation,
        Task? handlerTask)
    {
        try
        {
            await handlerCancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Canceling durable output handler for {MessageId} from {Address} failed with {HandlerCancellationExceptionType}.",
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value,
                exception.GetType().FullName);
        }

        if (handlerTask is null)
            return;

        try
        {
            await handlerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (handlerCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                "Observed durable output handler {HandlerExceptionType} after canceling delivery of {MessageId} from {Address}.",
                exception.GetType().FullName,
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value);
        }
    }

    private async ValueTask CompleteAsync(
        DurableOutputDeliveryLease lease,
        CancellationToken cancellationToken)
    {
        var transition = new DurableOutputDeliveryTransition(
            lease.Envelope.Key,
            lease.LeaseToken,
            _clock.GetUtcNow());
        var result = await InvokeStoreAsync(
                "complete",
                () => _store.CompleteAsync(transition, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        ValidateTransitionKey("complete", lease, result);
        DurableOutputInstrumentation.RecordDelivery("completed", result.IsApplied);
        if (result.IsApplied)
        {
            _logger.LogInformation(
                "Delivered durable output {MessageId} from {Address} on attempt {Attempt}.",
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value,
                lease.Attempt);
            return;
        }

        _logger.LogDebug(
            "Could not complete durable output {MessageId} from {Address}: {TransitionStatus}.",
            lease.Envelope.MessageId,
            lease.Envelope.Address.Value,
            result.Status);
    }

    private async ValueTask RetryAsync(
        DurableOutputDeliveryLease lease,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var retry = new DurableOutputDeliveryRetry(
            lease.Envelope.Key,
            lease.LeaseToken,
            now,
            now + _options.RetryDelay);
        var result = await InvokeStoreAsync(
                "retry",
                () => _store.RetryAsync(retry, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        ValidateTransitionKey("retry", lease, result);
        DurableOutputInstrumentation.RecordDelivery("retry", result.IsApplied);
        if (result.IsApplied)
        {
            _logger.LogDebug(
                "Scheduled durable output {MessageId} from {Address} for retry.",
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value);
            return;
        }

        _logger.LogDebug(
            "Could not retry durable output {MessageId} from {Address}: {TransitionStatus}.",
            lease.Envelope.MessageId,
            lease.Envelope.Address.Value,
            result.Status);
    }

    private async ValueTask DeadLetterAsync(
        DurableOutputDeliveryLease lease,
        CancellationToken cancellationToken)
    {
        var deadLetter = new DurableOutputDeliveryDeadLetter(
            lease.Envelope.Key,
            lease.LeaseToken,
            _clock.GetUtcNow(),
            DurableOutputDeadLetterReason.HandlerFailure);
        var result = await InvokeStoreAsync(
                "dead-letter",
                () => _store.DeadLetterAsync(deadLetter, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        ValidateTransitionKey("dead-letter", lease, result);
        DurableOutputInstrumentation.RecordDelivery("dead_letter", result.IsApplied);
        if (result.IsApplied)
        {
            _logger.LogWarning(
                "Dead-lettered durable output {MessageId} from {Address} on attempt {Attempt} with reason {DeadLetterReason}.",
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value,
                lease.Attempt,
                deadLetter.Reason);
            return;
        }

        _logger.LogDebug(
            "Could not dead-letter durable output {MessageId} from {Address}: {TransitionStatus}.",
            lease.Envelope.MessageId,
            lease.Envelope.Address.Value,
            result.Status);
    }

    private static void ValidateLease(
        DurableOutputDeliveryLeaseRequest request,
        DurableOutputDeliveryLease lease)
    {
        if (!string.Equals(lease.OwnerId, request.OwnerId, StringComparison.Ordinal) ||
            !HasExactValue(lease.LeasedAt, request.Now) ||
            !HasExactValue(lease.LeaseUntil, request.LeaseUntil))
        {
            throw new DurableOutputDeliveryStoreException(
                "lease",
                new InvalidOperationException(
                    "The durable output delivery store returned lease ownership that differs from the request."));
        }
    }

    private static void ValidateTransitionKey(
        string operation,
        DurableOutputDeliveryLease lease,
        DurableOutputDeliveryTransitionResult result)
    {
        if (result.Key != lease.Envelope.Key)
        {
            throw new DurableOutputDeliveryStoreException(
                operation,
                new InvalidOperationException(
                    "The durable output delivery store returned a transition result for a different key."));
        }
    }

    private static bool HasExactValue(DateTimeOffset left, DateTimeOffset right)
        => left.UtcTicks == right.UtcTicks && left.Offset == right.Offset;

    private static IDurableOutputDeliveryStore GetRequiredStore(
        IEnumerable<IDurableOutputDeliveryStore> stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        var candidates = stores.Take(2).ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                "AddFluxFlowDurableOutputDelivery requires one IDurableOutputDeliveryStore registration."),
            _ => throw new InvalidOperationException(
                "AddFluxFlowDurableOutputDelivery supports exactly one IDurableOutputDeliveryStore registration.")
        };
    }

    private static IDurableOutputDeliveryHandler GetRequiredHandler(
        IEnumerable<IDurableOutputDeliveryHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var candidates = handlers.Take(2).ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                "AddFluxFlowDurableOutputDelivery requires one IDurableOutputDeliveryHandler registration."),
            _ => throw new InvalidOperationException(
                "AddFluxFlowDurableOutputDelivery supports exactly one IDurableOutputDeliveryHandler registration.")
        };
    }

    private static async ValueTask<TResult> InvokeStoreAsync<TResult>(
        string operation,
        Func<ValueTask<TResult>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DurableOutputDeliveryStoreException)
        {
            throw;
        }
        catch (Exception exception)
        {
            DurableOutputInstrumentation.RecordStoreFailure(operation);
            throw new DurableOutputDeliveryStoreException(operation, exception);
        }
    }

    internal sealed class DurableOutputDeliveryStoreException(
        string operation,
        Exception innerException)
        : Exception(
            $"Durable output delivery store operation '{operation}' failed.",
            innerException)
    {
        public string Operation { get; } = operation;
    }
}
