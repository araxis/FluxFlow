using System.Text.Json;
using FluxFlow.Engine.Ports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.DurableInput;

internal sealed class DurableInputDispatcher : BackgroundService
{
    private readonly IDurableInputStore _store;
    private readonly DurableInputContractRegistry _contracts;
    private readonly FluxFlowApplication _application;
    private readonly DurableInputOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DurableInputDispatcher> _logger;
    private readonly IDurableInputCompletionSource? _completionSource;
    private readonly IDurableInputLeaseRenewalStore? _renewalStore;
    private readonly string _ownerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():n}";

    public DurableInputDispatcher(
        IDurableInputStore store,
        IEnumerable<IDurableInputCompletionSource> completionSources,
        IEnumerable<IDurableInputLeaseRenewalStore> renewalStores,
        DurableInputContractRegistry contracts,
        FluxFlowApplication application,
        DurableInputOptions options,
        TimeProvider clock,
        ILogger<DurableInputDispatcher> logger)
        : this(
            store,
            contracts,
            application,
            options,
            clock,
            logger,
            ResolveCapability(completionSources, options, "IDurableInputCompletionSource"),
            ResolveCapability(renewalStores, options, "IDurableInputLeaseRenewalStore"))
    {
    }

    internal DurableInputDispatcher(
        IDurableInputStore store,
        DurableInputContractRegistry contracts,
        FluxFlowApplication application,
        DurableInputOptions options,
        TimeProvider clock,
        ILogger<DurableInputDispatcher> logger)
        : this(
            store,
            contracts,
            application,
            options,
            clock,
            logger,
            completionSource: null,
            renewalStore: null)
    {
    }

    internal DurableInputDispatcher(
        IDurableInputStore store,
        DurableInputContractRegistry contracts,
        FluxFlowApplication application,
        DurableInputOptions options,
        TimeProvider clock,
        ILogger<DurableInputDispatcher> logger,
        IDurableInputCompletionSource? completionSource,
        IDurableInputLeaseRenewalStore? renewalStore)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _completionSource = completionSource;
        _renewalStore = renewalStore;
        if (options.AcknowledgementMode == DurableInputAcknowledgementMode.WorkflowCompleted)
        {
            _completionSource = completionSource ?? throw MissingCapability(
                "IDurableInputCompletionSource");
            _renewalStore = renewalStore ?? throw MissingCapability(
                "IDurableInputLeaseRenewalStore");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Durable input dispatcher {LeaseOwner} started.", _ownerId);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = _options.PollInterval;
                try
                {
                    if (await ProcessOnceAsync(stoppingToken).ConfigureAwait(false))
                        continue;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (DurableInputStoreException exception)
                {
                    delay = _options.StoreFailureDelay;
                    _logger.LogError(
                        exception.InnerException ?? exception,
                        "Durable input store operation {StoreOperation} failed; leases recover after expiry.",
                        exception.Operation);
                }

                try
                {
                    await Task.Delay(delay, _clock, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _logger.LogInformation("Durable input dispatcher {LeaseOwner} stopped.", _ownerId);
        }
    }

    internal async ValueTask<bool> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var request = new DurableInputLeaseRequest(
            _ownerId,
            now,
            now + _options.LeaseDuration,
            _options.AcknowledgementMode == DurableInputAcknowledgementMode.WorkflowCompleted
                ? 1
                : _options.BatchSize);
        var leases = await InvokeStoreAsync(
                "lease",
                () => _store.LeaseAsync(request, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        if (leases is null)
        {
            throw new DurableInputStoreException(
                "lease",
                new InvalidOperationException("The durable input store returned null leases."));
        }

        foreach (var lease in leases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DurableInputInstrumentation.RecordLeaseAcquired();
            _logger.LogDebug(
                "Leased durable input {MessageId} at {Address} for attempt {Attempt} by {LeaseOwner}.",
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value,
                lease.Attempt,
                lease.OwnerId);
            var durationStartedAt = DurableInputInstrumentation.StartDuration(_clock);
            var activity = DurableInputInstrumentation.StartProcessActivity(
                lease,
                _options.AcknowledgementMode);
            try
            {
                await ProcessLeaseAsync(lease, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DurableInputInstrumentation.SetActivityFailure(activity, "canceled");
                throw;
            }
            catch
            {
                DurableInputInstrumentation.SetActivityFailure(activity, "failed");
                throw;
            }
            finally
            {
                DurableInputInstrumentation.RecordDuration(_clock, durationStartedAt);
                DurableInputInstrumentation.StopActivity(activity);
            }
        }

        return leases.Count > 0;
    }

    private async ValueTask ProcessLeaseAsync(
        DurableInputLease lease,
        CancellationToken cancellationToken)
    {
        var envelope = lease.Envelope;
        if (envelope.SchemaVersion != DurableInputEnvelope.CurrentSchemaVersion)
        {
            await DeadLetterAsync(
                lease,
                Failure(
                    DurableInputFailureKind.UnsupportedSchemaVersion,
                    $"Envelope schema version {envelope.SchemaVersion} is unsupported."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_contracts.TryGetByName(envelope.ContractName, out var contract))
        {
            await DeadLetterAsync(
                lease,
                Failure(
                    DurableInputFailureKind.UnknownContract,
                    $"Contract '{envelope.ContractName}' is not registered."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        ApplicationPortMetadata? port;
        try
        {
            port = _application.Ports.Metadata.SingleOrDefault(item => item.Address == envelope.Address);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogDebug(
                exception,
                "Application ports are not currently available for durable input {MessageId} at {Address}.",
                envelope.MessageId,
                envelope.Address.Value);
            await RetryAsync(
                lease,
                Failure(DurableInputFailureKind.InputUnavailable, "Application ports are unavailable."),
                cancellationToken).ConfigureAwait(false);
            return;
        }
        if (port is null)
        {
            await RetryAsync(
                lease,
                Failure(
                    DurableInputFailureKind.InputAddressMissing,
                    $"Input address '{envelope.Address}' does not exist in the active revision."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (port.Direction != ApplicationPortDirection.Input ||
            port.Kind != ApplicationPortKind.Message)
        {
            await DeadLetterAsync(
                lease,
                Failure(
                    DurableInputFailureKind.NotMessageInput,
                    $"Address '{envelope.Address}' is not a message input."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (port.PayloadType != contract!.PayloadType)
        {
            await DeadLetterAsync(
                lease,
                Failure(
                    DurableInputFailureKind.PayloadTypeMismatch,
                    $"Contract '{contract.Name}' does not match the current input payload type."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        CompletionWait? completionWait = null;
        if (_options.AcknowledgementMode == DurableInputAcknowledgementMode.WorkflowCompleted)
        {
            completionWait = await SubscribeForCompletionAsync(lease, cancellationToken)
                .ConfigureAwait(false);
            if (completionWait is null)
                return;
        }

        try
        {
            PortSendResult sendResult;
            try
            {
                sendResult = await contract.RestoreAndSendAsync(
                        _application,
                        envelope,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
            {
                _logger.LogWarning(
                    exception,
                    "Durable input {MessageId} using contract {ContractName} could not be restored.",
                    envelope.MessageId,
                    envelope.ContractName);
                await DeadLetterAsync(
                    lease,
                    Failure(DurableInputFailureKind.DeserializationFailed, "The persisted payload could not be restored."),
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
                _logger.LogDebug(
                    exception,
                    "Application input {Address} became unavailable while dispatching {MessageId}.",
                    envelope.Address.Value,
                    envelope.MessageId);
                await RetryAsync(
                    lease,
                    Failure(DurableInputFailureKind.InputUnavailable, "The input became unavailable during dispatch."),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            switch (sendResult.Status)
            {
                case PortSendStatus.Accepted:
                    _logger.LogInformation(
                        "Engine input {Address} accepted durable message {MessageId} on attempt {Attempt}.",
                        envelope.Address.Value,
                        envelope.MessageId,
                        lease.Attempt);
                    if (completionWait is null)
                    {
                        await MarkDeliveredAsync(lease, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await WaitForWorkflowCompletionAsync(
                                lease,
                                completionWait.Completion,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    return;
                case PortSendStatus.Full:
                    await RetryAsync(
                        lease,
                        Failure(DurableInputFailureKind.InputFull, "The input was full."),
                        cancellationToken).ConfigureAwait(false);
                    return;
                case PortSendStatus.Unavailable:
                    await RetryAsync(
                        lease,
                        Failure(DurableInputFailureKind.InputUnavailable, "The input was unavailable."),
                        cancellationToken).ConfigureAwait(false);
                    return;
                case PortSendStatus.Completed:
                    await RetryAsync(
                        lease,
                        Failure(DurableInputFailureKind.InputCompleted, "The input was completed."),
                        cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new InvalidOperationException($"Unknown port send status '{sendResult.Status}'.");
            }
        }
        finally
        {
            if (completionWait is not null)
            {
                await DisposeCompletionSubscriptionAsync(
                        lease,
                        completionWait.Subscription)
                    .ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<CompletionWait?> SubscribeForCompletionAsync(
        DurableInputLease lease,
        CancellationToken cancellationToken)
    {
        IDurableInputCompletionSubscription? subscription = null;
        try
        {
            subscription = await _completionSource!
                .SubscribeAsync(lease, cancellationToken)
                .ConfigureAwait(false);
            if (subscription is null)
            {
                throw new InvalidOperationException(
                    "The durable input completion source returned a null subscription.");
            }

            var completion = subscription.Completion;
            if (completion is null)
            {
                throw new InvalidOperationException(
                    "The durable input completion subscription returned a null completion task.");
            }

            return new CompletionWait(subscription, completion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Durable input completion subscription failed for {MessageId} at {Address} with {ExceptionType}.",
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value,
                exception.GetType().FullName);
            if (subscription is not null)
                await DisposeCompletionSubscriptionAsync(lease, subscription).ConfigureAwait(false);
            await RetryAsync(
                    lease,
                    Failure(
                        DurableInputFailureKind.CompletionSourceUnavailable,
                        "The workflow completion source was unavailable."),
                    cancellationToken)
                .ConfigureAwait(false);
            return null;
        }
    }

    private async ValueTask WaitForWorkflowCompletionAsync(
        DurableInputLease lease,
        Task<DurableInputCompletionResult> completion,
        CancellationToken cancellationToken)
    {
        var startedAt = _clock.GetUtcNow();
        var hasTimeout = _options.WorkflowCompletionTimeout != Timeout.InfiniteTimeSpan;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (completion.IsCompleted)
            {
                await SettleWorkflowCompletionAsync(lease, completion, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var now = _clock.GetUtcNow();
            var elapsed = now > startedAt ? now - startedAt : TimeSpan.Zero;
            if (hasTimeout && elapsed >= _options.WorkflowCompletionTimeout)
            {
                await RetryAsync(
                        lease,
                        Failure(
                            DurableInputFailureKind.WorkflowCompletionTimedOut,
                            "The workflow completion signal timed out."),
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var delay = _options.LeaseRenewalInterval;
            if (hasTimeout)
            {
                var remaining = _options.WorkflowCompletionTimeout - elapsed;
                if (remaining < delay)
                    delay = remaining;
            }

            await Task.WhenAny(
                    completion,
                    Task.Delay(delay, _clock, cancellationToken))
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (completion.IsCompleted)
            {
                await SettleWorkflowCompletionAsync(lease, completion, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            now = _clock.GetUtcNow();
            elapsed = now > startedAt ? now - startedAt : TimeSpan.Zero;
            if (hasTimeout && elapsed >= _options.WorkflowCompletionTimeout)
            {
                await RetryAsync(
                        lease,
                        Failure(
                            DurableInputFailureKind.WorkflowCompletionTimedOut,
                            "The workflow completion signal timed out."),
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var renewal = new DurableInputLeaseRenewal(
                lease.Envelope.Key,
                lease.LeaseToken,
                now,
                now + _options.LeaseDuration);
            var result = await InvokeStoreAsync(
                    "renew-lease",
                    () => _renewalStore!.RenewLeaseAsync(renewal, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            LogTransition("renew lease", lease, result);
            DurableInputInstrumentation.RecordLeaseRenewal(result.IsApplied);
            if (!result.IsApplied)
            {
                _logger.LogDebug(
                    "Stopped waiting for workflow completion of durable input {MessageId} at {Address} because its lease is no longer current.",
                    lease.Envelope.MessageId,
                    lease.Envelope.Address.Value);
                return;
            }
        }
    }

    private async ValueTask SettleWorkflowCompletionAsync(
        DurableInputLease lease,
        Task<DurableInputCompletionResult> completion,
        CancellationToken cancellationToken)
    {
        DurableInputCompletionResult? result;
        try
        {
            result = await completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Workflow completion failed for durable input {MessageId} at {Address} with {ExceptionType}.",
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value,
                exception.GetType().FullName);
            await RetryAsync(
                    lease,
                    Failure(
                        DurableInputFailureKind.WorkflowCompletionFailed,
                        "The workflow completion signal failed."),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (result is null)
        {
            await RetryAsync(
                    lease,
                    Failure(
                        DurableInputFailureKind.CompletionSourceUnavailable,
                        "The workflow completion source returned no result."),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (result.IsCompleted)
        {
            await MarkDeliveredAsync(lease, cancellationToken).ConfigureAwait(false);
            return;
        }

        await RetryAsync(
                lease,
                Failure(
                    DurableInputFailureKind.WorkflowCompletionFailed,
                    result.FailureDescription!),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask DisposeCompletionSubscriptionAsync(
        DurableInputLease lease,
        IDurableInputCompletionSubscription subscription)
    {
        try
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Disposing the workflow completion subscription for durable input {MessageId} at {Address} failed with {ExceptionType}.",
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value,
                exception.GetType().FullName);
        }
    }

    private async ValueTask MarkDeliveredAsync(
        DurableInputLease lease,
        CancellationToken cancellationToken)
    {
        var transition = new DurableInputLeaseTransition(
            lease.Envelope.Key,
            lease.LeaseToken,
            _clock.GetUtcNow());
        var result = await InvokeStoreAsync(
                "mark-delivered",
                () => _store.MarkDeliveredAsync(transition, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        LogTransition("mark delivered", lease, result);
        if (result.IsApplied)
        {
            DurableInputInstrumentation.RecordMessage("delivered");
            _logger.LogInformation(
                "Delivered durable input {MessageId} to {Address} on attempt {Attempt}.",
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value,
                lease.Attempt);
        }
    }

    private async ValueTask RetryAsync(
        DurableInputLease lease,
        DurableInputFailure failure,
        CancellationToken cancellationToken)
    {
        if (lease.Attempt >= _options.MaxDeliveryAttempts)
        {
            await DeadLetterAsync(
                lease,
                Failure(
                    DurableInputFailureKind.MaximumAttemptsExceeded,
                    $"Maximum attempts reached after '{failure.Kind}'."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var now = _clock.GetUtcNow();
        var release = new DurableInputRelease(
            lease.Envelope.Key,
            lease.LeaseToken,
            now,
            now + _options.RetryDelay,
            failure);
        var result = await InvokeStoreAsync(
                "release",
                () => _store.ReleaseAsync(release, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        LogTransition("release", lease, result);
        if (result.IsApplied)
        {
            DurableInputInstrumentation.RecordMessage("retry", failure.Kind);
            _logger.LogDebug(
                "Scheduled durable input {MessageId} at {Address} for retry after {FailureKind}.",
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value,
                failure.Kind);
        }
    }

    private async ValueTask DeadLetterAsync(
        DurableInputLease lease,
        DurableInputFailure failure,
        CancellationToken cancellationToken)
    {
        var deadLetter = new DurableInputDeadLetter(
            lease.Envelope.Key,
            lease.LeaseToken,
            _clock.GetUtcNow(),
            failure);
        var result = await InvokeStoreAsync(
                "dead-letter",
                () => _store.DeadLetterAsync(deadLetter, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        LogTransition("dead-letter", lease, result);
        if (result.IsApplied)
        {
            DurableInputInstrumentation.RecordMessage("dead_letter", failure.Kind);
            _logger.LogWarning(
                "Dead-lettered durable input {MessageId} at {Address} after attempt {Attempt}: {FailureKind}.",
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value,
                lease.Attempt,
                failure.Kind);
        }
    }

    private void LogTransition(
        string operation,
        DurableInputLease lease,
        DurableInputTransitionResult result)
    {
        if (result is null)
        {
            throw new DurableInputStoreException(
                operation,
                new InvalidOperationException(
                    "The durable input store returned a null transition result."));
        }

        if (result.Key != lease.Envelope.Key)
        {
            throw new DurableInputStoreException(
                operation,
                new InvalidOperationException(
                    "The durable input store returned a transition result for a different key."));
        }

        if (!result.IsApplied)
        {
            _logger.LogDebug(
                "Could not {Operation} durable input {MessageId} at {Address}: {TransitionStatus}.",
                operation,
                lease.Envelope.MessageId,
                lease.Envelope.Address.Value,
                result.Status);
        }
    }

    private static TCapability? ResolveCapability<TCapability>(
        IEnumerable<TCapability> capabilities,
        DurableInputOptions options,
        string capabilityName)
        where TCapability : class
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(options);
        if (options.AcknowledgementMode != DurableInputAcknowledgementMode.WorkflowCompleted)
            return null;

        using var enumerator = capabilities.GetEnumerator();
        if (!enumerator.MoveNext())
            throw MissingCapability(capabilityName);

        var capability = enumerator.Current ?? throw MissingCapability(capabilityName);
        if (enumerator.MoveNext())
        {
            throw new InvalidOperationException(
                $"Workflow-completion durable input requires exactly one {capabilityName} registration, but multiple registrations were found.");
        }

        return capability;
    }

    private static InvalidOperationException MissingCapability(string capabilityName)
        => new(
            $"Workflow-completion durable input requires exactly one {capabilityName} registration, but none was found.");

    private static DurableInputFailure Failure(
        DurableInputFailureKind kind,
        string description)
        => new(kind, description);

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
        catch (Exception exception)
        {
            DurableInputInstrumentation.RecordStoreFailure(operation);
            throw new DurableInputStoreException(operation, exception);
        }
    }

    private sealed class DurableInputStoreException(string operation, Exception innerException)
        : Exception($"Durable input store operation '{operation}' failed.", innerException)
    {
        public string Operation { get; } = operation;
    }

    private sealed record CompletionWait(
        IDurableInputCompletionSubscription Subscription,
        Task<DurableInputCompletionResult> Completion);
}
