using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputDeliveryDispatcherTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 10, 0, 0, TimeSpan.FromHours(2));

    private static readonly DurableOutputDeliveryOptions Options = new(
        leaseDuration: TimeSpan.FromSeconds(30),
        leaseRenewalInterval: TimeSpan.FromSeconds(10),
        retryDelay: TimeSpan.FromSeconds(4),
        idleDelay: TimeSpan.FromMilliseconds(250));

    private static DurableOutputDeliveryOptions Limited(int maximum)
        => new(
            Options.LeaseDuration,
            Options.LeaseRenewalInterval,
            Options.RetryDelay,
            Options.IdleDelay,
            maxDeliveryAttempts: maximum);

    [Fact]
    public async Task Empty_store_returns_false_after_one_exact_lease_request()
    {
        var store = new ScriptedDeliveryStore();
        var dispatcher = Dispatcher(store, new ScriptedDeliveryHandler(), new FakeTimeProvider(Now));

        var processed = await dispatcher.ProcessOnceAsync();

        processed.ShouldBeFalse();
        var request = store.LeaseRequests.ShouldHaveSingleItem();
        request.OwnerId.ShouldNotBeNullOrWhiteSpace();
        request.OwnerId.ShouldBe(request.OwnerId.Trim());
        request.Now.ShouldBe(Now);
        request.LeaseUntil.ShouldBe(Now + Options.LeaseDuration);
        store.Renewals.ShouldBeEmpty();
        store.Completions.ShouldBeEmpty();
        store.Retries.ShouldBeEmpty();
        store.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handler_success_before_first_interval_completes_once_without_renewal()
    {
        var clock = new FakeTimeProvider(Now);
        var envelope = DurableOutputTestData.Envelope();
        var token = Guid.Parse("114cfe44-8d3f-4c9e-a7cf-c9488ab2f51c");
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, token, attempt: 2))
        };
        var handler = new ScriptedDeliveryHandler();
        var dispatcher = Dispatcher(store, handler, clock);

        var processed = await dispatcher.ProcessOnceAsync();

        processed.ShouldBeTrue();
        handler.Envelopes.ShouldHaveSingleItem().ShouldBeSameAs(envelope);
        var completion = store.Completions.ShouldHaveSingleItem();
        completion.Key.ShouldBe(envelope.Key);
        completion.LeaseToken.ShouldBe(token);
        completion.OccurredAt.ShouldBe(Now);
        store.Renewals.ShouldBeEmpty();
        store.Retries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Long_handler_renews_multiple_exact_intervals_without_reinvocation_then_completes_once()
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var envelope = DurableOutputTestData.Envelope();
        var token = Guid.Parse("09611e28-84d3-48c7-a37f-e6835324561b");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRenewal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRenewal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalCalls = 0;
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, token, attempt: 4)),
            OnRenew = (renewal, _) =>
            {
                if (Interlocked.Increment(ref renewalCalls) == 1)
                    firstRenewal.TrySetResult();
                else
                    secondRenewal.TrySetResult();
                return ValueTask.FromResult(new DurableOutputDeliveryTransitionResult(
                    renewal.Key,
                    DurableOutputDeliveryTransitionStatus.Applied));
            }
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        };
        var dispatcher = Dispatcher(store, handler, clock);
        var operation = dispatcher.ProcessOnceAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        clock.Advance(Options.LeaseRenewalInterval);
        await firstRenewal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(Options.LeaseRenewalInterval);
        await secondRenewal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult();

        (await operation.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        var renewals = store.Renewals.ToArray();
        renewals.Length.ShouldBe(2);
        renewals[0].ShouldBe(new DurableOutputDeliveryLeaseRenewal(
            envelope.Key,
            token,
            Now + Options.LeaseRenewalInterval,
            Now + Options.LeaseRenewalInterval + Options.LeaseDuration));
        renewals[1].ShouldBe(new DurableOutputDeliveryLeaseRenewal(
            envelope.Key,
            token,
            Now + (Options.LeaseRenewalInterval * 2),
            Now + (Options.LeaseRenewalInterval * 2) + Options.LeaseDuration));
        handler.Envelopes.ShouldHaveSingleItem().ShouldBeSameAs(envelope);
        store.LeaseRequests.ShouldHaveSingleItem();
        var completion = store.Completions.ShouldHaveSingleItem();
        completion.Key.ShouldBe(envelope.Key);
        completion.LeaseToken.ShouldBe(token);
        completion.OccurredAt.ShouldBe(Now + (Options.LeaseRenewalInterval * 2));
        store.Retries.ShouldBeEmpty();
        store.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handler_completion_winning_the_tick_boundary_skips_renewal()
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var envelope = DurableOutputTestData.Envelope();
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid()))
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = (_, _) => new ValueTask(complete.Task)
        };
        var operation = Dispatcher(store, handler, clock).ProcessOnceAsync().AsTask();
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        complete.TrySetResult();
        clock.Advance(Options.LeaseRenewalInterval);

        (await operation.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        store.Renewals.ShouldBeEmpty();
        store.Completions.ShouldHaveSingleItem();
        handler.Envelopes.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handler_failure_after_renewal_retries_at_the_current_clock_delay()
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var envelope = DurableOutputTestData.Envelope();
        var token = Guid.Parse("afc5023d-d4da-4864-87c8-69a599283b8e");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, token, attempt: 3)),
            OnRenew = (renewal, _) =>
            {
                renewalObserved.TrySetResult();
                return ValueTask.FromResult(new DurableOutputDeliveryTransitionResult(
                    renewal.Key,
                    DurableOutputDeliveryTransitionStatus.Applied));
            }
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await fail.Task.WaitAsync(cancellationToken);
            }
        };
        var operation = Dispatcher(store, handler, clock).ProcessOnceAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        clock.Advance(Options.LeaseRenewalInterval);
        await renewalObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fail.TrySetException(new IOException("handler failed after renewal"));

        (await operation.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        store.Renewals.ShouldHaveSingleItem();
        var retry = store.Retries.ShouldHaveSingleItem();
        retry.Key.ShouldBe(envelope.Key);
        retry.LeaseToken.ShouldBe(token);
        retry.ReleasedAt.ShouldBe(Now + Options.LeaseRenewalInterval);
        retry.NextAttemptAt.ShouldBe(
            Now + Options.LeaseRenewalInterval + Options.RetryDelay);
        store.Completions.ShouldBeEmpty();
        store.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Final_attempt_handler_failure_after_renewal_dead_letters_once()
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var envelope = DurableOutputTestData.Envelope();
        var token = Guid.Parse("cc6cbec9-726c-417b-b035-abdf344ec479");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, token, attempt: 3)),
            OnRenew = (renewal, _) =>
            {
                renewalObserved.TrySetResult();
                return ValueTask.FromResult(new DurableOutputDeliveryTransitionResult(
                    renewal.Key,
                    DurableOutputDeliveryTransitionStatus.Applied));
            }
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await fail.Task.WaitAsync(cancellationToken);
            }
        };
        var operation = Dispatcher(store, handler, clock, options: Limited(3))
            .ProcessOnceAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        clock.Advance(Options.LeaseRenewalInterval);
        await renewalObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fail.TrySetException(new InvalidOperationException("final handler failure"));

        (await operation.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        store.Renewals.ShouldHaveSingleItem();
        var deadLetter = store.DeadLetters.ShouldHaveSingleItem();
        deadLetter.Key.ShouldBe(envelope.Key);
        deadLetter.LeaseToken.ShouldBe(token);
        deadLetter.DeadLetteredAt.ShouldBe(Now + Options.LeaseRenewalInterval);
        deadLetter.Reason.ShouldBe(DurableOutputDeadLetterReason.HandlerFailure);
        store.Completions.ShouldBeEmpty();
        store.Retries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DurableOutputDeliveryTransitionStatus.LeaseLost)]
    [InlineData(DurableOutputDeliveryTransitionStatus.NotFound)]
    [InlineData(DurableOutputDeliveryTransitionStatus.InvalidState)]
    public async Task Nonapplied_renewal_cancels_and_observes_handler_without_stale_settlement(
        DurableOutputDeliveryTransitionStatus status)
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var envelope = DurableOutputTestData.Envelope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid())),
            OnRenew = (renewal, _) => ValueTask.FromResult(
                new DurableOutputDeliveryTransitionResult(renewal.Key, status))
        };
        var handler = CancellableHandler(entered, observed);
        var operation = Dispatcher(store, handler, clock).ProcessOnceAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        clock.Advance(Options.LeaseRenewalInterval);

        (await operation.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.Renewals.ShouldHaveSingleItem();
        handler.Envelopes.ShouldHaveSingleItem();
        store.LeaseRequests.ShouldHaveSingleItem();
        AssertUnsettled(store);
    }

    [Fact]
    public async Task Wrong_renewal_result_key_is_sanitized_and_observes_the_handler()
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var envelope = DurableOutputTestData.Envelope();
        var wrongKey = DurableOutputTestData.Envelope(
            messageId: new FluxFlow.Nodes.MessageId("wrong-renewal-key")).Key;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid())),
            OnRenew = (_, _) => ValueTask.FromResult(new DurableOutputDeliveryTransitionResult(
                wrongKey,
                DurableOutputDeliveryTransitionStatus.Applied))
        };
        var handler = CancellableHandler(entered, observed);
        var operation = Dispatcher(store, handler, clock).ProcessOnceAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(Options.LeaseRenewalInterval);

        var exception = await Should.ThrowAsync<DurableOutputDeliveryDispatcher.DurableOutputDeliveryStoreException>(
            () => operation);

        exception.Operation.ShouldBe("renew-lease");
        exception.Message.ShouldNotContain(envelope.Payload.GetRawText());
        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("different key");
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.Renewals.ShouldHaveSingleItem();
        handler.Envelopes.ShouldHaveSingleItem();
        AssertUnsettled(store);
    }

    [Fact]
    public async Task Renewal_store_failure_is_sanitized_cancels_observes_and_leaves_unsettled()
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var envelope = DurableOutputTestData.Envelope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("private renewal database detail");
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid())),
            OnRenew = (_, _) => ValueTask.FromException<DurableOutputDeliveryTransitionResult>(failure)
        };
        var handler = CancellableHandler(entered, observed);
        var operation = Dispatcher(store, handler, clock).ProcessOnceAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(Options.LeaseRenewalInterval);

        var exception = await Should.ThrowAsync<DurableOutputDeliveryDispatcher.DurableOutputDeliveryStoreException>(
            () => operation);

        exception.Operation.ShouldBe("renew-lease");
        exception.InnerException.ShouldBeSameAs(failure);
        exception.Message.ShouldNotContain("private renewal database detail");
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.Renewals.ShouldHaveSingleItem();
        handler.Envelopes.ShouldHaveSingleItem();
        AssertUnsettled(store);
    }

    [Fact]
    public async Task Host_cancellation_after_renewal_observes_handler_without_settlement()
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var envelope = DurableOutputTestData.Envelope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid())),
            OnRenew = (renewal, _) =>
            {
                renewalObserved.TrySetResult();
                return ValueTask.FromResult(new DurableOutputDeliveryTransitionResult(
                    renewal.Key,
                    DurableOutputDeliveryTransitionStatus.Applied));
            }
        };
        var handler = CancellableHandler(entered, observed);
        using var cancellation = new CancellationTokenSource();
        var operation = Dispatcher(store, handler, clock)
            .ProcessOnceAsync(cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(Options.LeaseRenewalInterval);
        await renewalObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => operation);

        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.Renewals.ShouldHaveSingleItem();
        handler.Envelopes.ShouldHaveSingleItem();
        AssertUnsettled(store);
    }

    [Fact]
    public async Task Handler_exception_retries_at_the_exact_fixed_clock_relative_delay()
    {
        var clock = new FakeTimeProvider(Now);
        var envelope = DurableOutputTestData.Envelope();
        var token = Guid.Parse("7d9273de-395a-4518-9425-69ea6e48f1ce");
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, token, attempt: 3))
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = static (_, _) =>
                ValueTask.FromException(new IOException("destination unavailable"))
        };
        var dispatcher = Dispatcher(store, handler, clock);

        var processed = await dispatcher.ProcessOnceAsync();

        processed.ShouldBeTrue();
        store.Completions.ShouldBeEmpty();
        var retry = store.Retries.ShouldHaveSingleItem();
        retry.Key.ShouldBe(envelope.Key);
        retry.LeaseToken.ShouldBe(token);
        retry.ReleasedAt.ShouldBe(Now);
        retry.NextAttemptAt.ShouldBe(Now + Options.RetryDelay);
    }

    [Fact]
    public async Task Host_cancellation_during_handler_leaves_lease_for_expiry_without_transition()
    {
        var envelope = DurableOutputTestData.Envelope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid()))
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var dispatcher = Dispatcher(store, handler, new FakeTimeProvider(Now));
        using var cancellation = new CancellationTokenSource();
        var operation = dispatcher.ProcessOnceAsync(cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => operation);

        store.Completions.ShouldBeEmpty();
        store.Retries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Wrong_transition_result_key_fails_without_false_success()
    {
        var envelope = DurableOutputTestData.Envelope();
        var wrongKey = DurableOutputTestData.Envelope(
            messageId: new FluxFlow.Nodes.MessageId("other-message")).Key;
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid())),
            CompleteStatus = transition => new DurableOutputDeliveryTransitionResult(
                wrongKey,
                DurableOutputDeliveryTransitionStatus.Applied)
        };
        var logger = new RecordingLogger<DurableOutputDeliveryDispatcher>();
        var dispatcher = Dispatcher(
            store,
            new ScriptedDeliveryHandler(),
            new FakeTimeProvider(Now),
            logger);

        var exception = await Should.ThrowAsync<DurableOutputDeliveryDispatcher.DurableOutputDeliveryStoreException>(
            () => dispatcher.ProcessOnceAsync().AsTask());

        exception.Operation.ShouldBe("complete");
        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("different key");
        logger.Entries.ShouldNotContain(static entry =>
            entry.Level == LogLevel.Information && entry.Message.Contains("Delivered durable output"));
    }

    [Theory]
    [InlineData(DurableOutputDeliveryTransitionStatus.LeaseLost)]
    [InlineData(DurableOutputDeliveryTransitionStatus.NotFound)]
    [InlineData(DurableOutputDeliveryTransitionStatus.InvalidState)]
    public async Task Nonapplied_completion_is_logged_without_false_delivery_success(
        DurableOutputDeliveryTransitionStatus status)
    {
        var envelope = DurableOutputTestData.Envelope();
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid())),
            CompleteStatus = transition =>
                new DurableOutputDeliveryTransitionResult(transition.Key, status)
        };
        var logger = new RecordingLogger<DurableOutputDeliveryDispatcher>();
        var dispatcher = Dispatcher(
            store,
            new ScriptedDeliveryHandler(),
            new FakeTimeProvider(Now),
            logger);

        (await dispatcher.ProcessOnceAsync()).ShouldBeTrue();

        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("Could not complete") &&
            entry.Message.Contains(status.ToString()));
        logger.Entries.ShouldNotContain(static entry =>
            entry.Level == LogLevel.Information && entry.Message.Contains("Delivered durable output"));
    }

    [Theory]
    [InlineData(DurableOutputDeliveryTransitionStatus.LeaseLost)]
    [InlineData(DurableOutputDeliveryTransitionStatus.NotFound)]
    [InlineData(DurableOutputDeliveryTransitionStatus.InvalidState)]
    public async Task Nonapplied_retry_is_logged_without_false_scheduled_success(
        DurableOutputDeliveryTransitionStatus status)
    {
        var envelope = DurableOutputTestData.Envelope();
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid())),
            RetryStatus = retry =>
                new DurableOutputDeliveryTransitionResult(retry.Key, status)
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = static (_, _) => ValueTask.FromException(new IOException("failed"))
        };
        var logger = new RecordingLogger<DurableOutputDeliveryDispatcher>();
        var dispatcher = Dispatcher(store, handler, new FakeTimeProvider(Now), logger);

        (await dispatcher.ProcessOnceAsync()).ShouldBeTrue();

        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("Could not retry") &&
            entry.Message.Contains(status.ToString()));
        logger.Entries.ShouldNotContain(static entry =>
            entry.Message.Contains("Scheduled durable output"));
        store.Completions.ShouldBeEmpty();
        store.Retries.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Wrong_retry_result_key_fails_without_false_scheduled_success()
    {
        var envelope = DurableOutputTestData.Envelope();
        var wrongKey = DurableOutputTestData.Envelope(
            messageId: new FluxFlow.Nodes.MessageId("other-message")).Key;
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid())),
            RetryStatus = _ => new DurableOutputDeliveryTransitionResult(
                wrongKey,
                DurableOutputDeliveryTransitionStatus.Applied)
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = static (_, _) => ValueTask.FromException(new IOException("failed"))
        };
        var logger = new RecordingLogger<DurableOutputDeliveryDispatcher>();
        var dispatcher = Dispatcher(store, handler, new FakeTimeProvider(Now), logger);

        var exception = await Should.ThrowAsync<DurableOutputDeliveryDispatcher.DurableOutputDeliveryStoreException>(
            () => dispatcher.ProcessOnceAsync().AsTask());

        exception.Operation.ShouldBe("retry");
        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("different key");
        logger.Entries.ShouldNotContain(static entry =>
            entry.Message.Contains("Scheduled durable output"));
        store.Completions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lease_ownership_mismatch_is_wrapped_as_a_lease_store_failure()
    {
        var envelope = DurableOutputTestData.Envelope();
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                new DurableOutputDeliveryLease(
                    envelope,
                    Guid.NewGuid(),
                    "different-owner",
                    request.Now,
                    request.LeaseUntil,
                    1))
        };
        var dispatcher = Dispatcher(store, new ScriptedDeliveryHandler(), new FakeTimeProvider(Now));

        var exception = await Should.ThrowAsync<DurableOutputDeliveryDispatcher.DurableOutputDeliveryStoreException>(
            () => dispatcher.ProcessOnceAsync().AsTask());

        exception.Operation.ShouldBe("lease");
        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("ownership that differs");
    }

    [Theory]
    [InlineData(LeaseTimeMismatch.LeasedAtInstant)]
    [InlineData(LeaseTimeMismatch.LeasedAtOffset)]
    [InlineData(LeaseTimeMismatch.LeaseUntilInstant)]
    [InlineData(LeaseTimeMismatch.LeaseUntilOffset)]
    public async Task Lease_time_or_offset_mismatch_is_rejected_exactly(
        LeaseTimeMismatch mismatch)
    {
        var envelope = DurableOutputTestData.Envelope();
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) =>
            {
                var leasedAt = mismatch switch
                {
                    LeaseTimeMismatch.LeasedAtInstant => request.Now.AddTicks(1),
                    LeaseTimeMismatch.LeasedAtOffset => request.Now.ToOffset(TimeSpan.Zero),
                    _ => request.Now
                };
                var leaseUntil = mismatch switch
                {
                    LeaseTimeMismatch.LeaseUntilInstant => request.LeaseUntil.AddTicks(1),
                    LeaseTimeMismatch.LeaseUntilOffset => request.LeaseUntil.ToOffset(TimeSpan.Zero),
                    _ => request.LeaseUntil
                };
                return ValueTask.FromResult<DurableOutputDeliveryLease?>(
                    new DurableOutputDeliveryLease(
                        envelope,
                        Guid.NewGuid(),
                        request.OwnerId,
                        leasedAt,
                        leaseUntil,
                        1));
            }
        };
        var dispatcher = Dispatcher(store, new ScriptedDeliveryHandler(), new FakeTimeProvider(Now));

        var exception = await Should.ThrowAsync<DurableOutputDeliveryDispatcher.DurableOutputDeliveryStoreException>(
            () => dispatcher.ProcessOnceAsync().AsTask());

        exception.Operation.ShouldBe("lease");
        exception.InnerException.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("ownership that differs");
    }

    [Fact]
    public async Task Hosted_empty_store_waits_exact_idle_delay_without_spinning()
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var store = new ScriptedDeliveryStore();
        var dispatcher = Dispatcher(store, new ScriptedDeliveryHandler(), clock);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await dispatcher.StartAsync(CancellationToken.None);
        await clock.TimerCreated.Task.WaitAsync(timeout.Token);

        store.LeaseRequests.Count.ShouldBe(1);
        clock.DueTimes.ShouldHaveSingleItem().ShouldBe(Options.IdleDelay);
        clock.Advance(Options.IdleDelay - TimeSpan.FromTicks(1));
        store.LeaseRequests.Count.ShouldBe(1);
        clock.Advance(TimeSpan.FromTicks(1));
        await store.SecondLeaseObserved.Task.WaitAsync(timeout.Token);
        store.LeaseRequests.Count.ShouldBe(2);

        await dispatcher.StopAsync(timeout.Token);
        dispatcher.ExecuteTask.ShouldNotBeNull().IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_store_failure_logs_waits_idle_delay_and_recovers()
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var calls = 0;
        var store = new ScriptedDeliveryStore
        {
            OnLease = (_, _) => Interlocked.Increment(ref calls) == 1
                ? ValueTask.FromException<DurableOutputDeliveryLease?>(
                    new IOException("database unavailable"))
                : ValueTask.FromResult<DurableOutputDeliveryLease?>(null)
        };
        var logger = new RecordingLogger<DurableOutputDeliveryDispatcher>();
        var dispatcher = Dispatcher(store, new ScriptedDeliveryHandler(), clock, logger);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await dispatcher.StartAsync(CancellationToken.None);
        await clock.TimerCreated.Task.WaitAsync(timeout.Token);

        store.LeaseRequests.Count.ShouldBe(1);
        clock.DueTimes.ShouldHaveSingleItem().ShouldBe(Options.IdleDelay);
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error &&
            entry.Message.Contains("store operation lease failed") &&
            entry.Message.Contains(typeof(IOException).FullName!, StringComparison.Ordinal) &&
            entry.Exception == null);
        clock.Advance(Options.IdleDelay);
        await store.SecondLeaseObserved.Task.WaitAsync(timeout.Token);
        store.LeaseRequests.Count.ShouldBe(2);

        await dispatcher.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Hosted_worker_never_starts_second_delivery_while_first_is_in_flight()
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var first = DurableOutputTestData.Envelope();
        var second = DurableOutputTestData.Envelope(
            messageId: new FluxFlow.Nodes.MessageId("message-2"));
        var leaseCalls = 0;
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => Interlocked.Increment(ref leaseCalls) switch
            {
                1 => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                    Lease(request, first, Guid.NewGuid())),
                2 => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                    Lease(request, second, Guid.NewGuid())),
                _ => ValueTask.FromResult<DurableOutputDeliveryLease?>(null)
            }
        };
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = async (envelope, cancellationToken) =>
            {
                if (envelope.Key == first.Key)
                {
                    firstEntered.TrySetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    secondEntered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
            }
        };
        var dispatcher = Dispatcher(store, handler, clock);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await dispatcher.StartAsync(CancellationToken.None);
        await firstEntered.Task.WaitAsync(timeout.Token);

        secondEntered.Task.IsCompleted.ShouldBeFalse();
        store.LeaseRequests.Count.ShouldBe(1);
        store.Completions.ShouldBeEmpty();
        releaseFirst.TrySetResult();
        await secondEntered.Task.WaitAsync(timeout.Token);

        handler.Envelopes.Select(static envelope => envelope.Key)
            .ShouldBe([first.Key, second.Key]);
        store.LeaseRequests.Count.ShouldBe(2);
        store.Completions.ShouldHaveSingleItem().Key.ShouldBe(first.Key);
        await dispatcher.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Unlimited_failure_retries_and_never_dead_letters()
    {
        var envelope = DurableOutputTestData.Envelope();
        var token = Guid.NewGuid();
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, token, attempt: 99))
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = static (_, _) => throw new InvalidOperationException("sensitive failure")
        };

        (await Dispatcher(store, handler, new FakeTimeProvider(Now)).ProcessOnceAsync())
            .ShouldBeTrue();

        store.Retries.ShouldHaveSingleItem().Key.ShouldBe(envelope.Key);
        store.DeadLetters.ShouldBeEmpty();
        store.Completions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Maximum_one_dead_letters_first_failure_with_exact_current_values()
    {
        var envelope = DurableOutputTestData.Envelope();
        var token = Guid.NewGuid();
        var clock = new FakeTimeProvider(Now);
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, token))
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = static (_, _) => throw new InvalidOperationException("handler failed")
        };

        (await Dispatcher(store, handler, clock, options: Limited(1)).ProcessOnceAsync())
            .ShouldBeTrue();

        var deadLetter = store.DeadLetters.ShouldHaveSingleItem();
        deadLetter.Key.ShouldBe(envelope.Key);
        deadLetter.LeaseToken.ShouldBe(token);
        deadLetter.DeadLetteredAt.ShouldBe(Now);
        deadLetter.DeadLetteredAt.Offset.ShouldBe(Now.Offset);
        deadLetter.Reason.ShouldBe(DurableOutputDeadLetterReason.HandlerFailure);
        store.Retries.ShouldBeEmpty();
        store.Completions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Maximum_n_retries_below_limit_and_dead_letters_at_limit()
    {
        var envelope = DurableOutputTestData.Envelope();
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = static (_, _) => throw new InvalidOperationException("handler failed")
        };
        var below = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid(), attempt: 2))
        };
        var final = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid(), attempt: 3))
        };

        await Dispatcher(below, handler, new FakeTimeProvider(Now), options: Limited(3))
            .ProcessOnceAsync();
        await Dispatcher(final, handler, new FakeTimeProvider(Now), options: Limited(3))
            .ProcessOnceAsync();

        below.Retries.ShouldHaveSingleItem();
        below.DeadLetters.ShouldBeEmpty();
        final.Retries.ShouldBeEmpty();
        final.DeadLetters.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Successful_limit_attempt_completes_without_dead_letter()
    {
        var envelope = DurableOutputTestData.Envelope();
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid(), attempt: 3))
        };

        await Dispatcher(
            store,
            new ScriptedDeliveryHandler(),
            new FakeTimeProvider(Now),
            options: Limited(3)).ProcessOnceAsync();

        store.Completions.ShouldHaveSingleItem().Key.ShouldBe(envelope.Key);
        store.DeadLetters.ShouldBeEmpty();
        store.Retries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_during_final_attempt_leaves_lease_unsettled()
    {
        var envelope = DurableOutputTestData.Envelope();
        using var cancellation = new CancellationTokenSource();
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid(), attempt: 1))
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = (_, token) =>
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled(token);
            }
        };

        await Should.ThrowAsync<OperationCanceledException>(() => Dispatcher(
            store,
            handler,
            new FakeTimeProvider(Now),
            options: Limited(1)).ProcessOnceAsync(cancellation.Token).AsTask());

        store.Completions.ShouldBeEmpty();
        store.Retries.ShouldBeEmpty();
        store.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Wrong_dead_letter_result_key_is_store_contract_failure()
    {
        var envelope = DurableOutputTestData.Envelope();
        var wrongKey = DurableOutputTestData.Envelope(
            messageId: new FluxFlow.Nodes.MessageId("wrong-dead-letter-key")).Key;
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid())),
            DeadLetterStatus = _ => new DurableOutputDeliveryTransitionResult(
                wrongKey,
                DurableOutputDeliveryTransitionStatus.Applied)
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = static (_, _) => throw new InvalidOperationException("handler failed")
        };

        var exception = await Should.ThrowAsync<DurableOutputDeliveryDispatcher.DurableOutputDeliveryStoreException>(
            () => Dispatcher(store, handler, new FakeTimeProvider(Now), options: Limited(1))
                .ProcessOnceAsync().AsTask());

        exception.Operation.ShouldBe("dead-letter");
        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
        store.DeadLetters.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(DurableOutputDeliveryTransitionStatus.LeaseLost)]
    [InlineData(DurableOutputDeliveryTransitionStatus.NotFound)]
    [InlineData(DurableOutputDeliveryTransitionStatus.InvalidState)]
    public async Task Nonapplied_dead_letter_is_logged_without_false_success(
        DurableOutputDeliveryTransitionStatus status)
    {
        var envelope = DurableOutputTestData.Envelope();
        var logger = new RecordingLogger<DurableOutputDeliveryDispatcher>();
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid())),
            DeadLetterStatus = deadLetter => new DurableOutputDeliveryTransitionResult(
                deadLetter.Key,
                status)
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = static (_, _) => throw new InvalidOperationException("handler failed")
        };

        await Dispatcher(store, handler, new FakeTimeProvider(Now), logger, Limited(1))
            .ProcessOnceAsync();

        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("Could not dead-letter", StringComparison.Ordinal) &&
            entry.Message.Contains(status.ToString(), StringComparison.Ordinal));
        logger.Entries.ShouldNotContain(entry =>
            entry.Message.StartsWith("Dead-lettered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dead_letter_transition_exception_is_named_store_failure()
    {
        var envelope = DurableOutputTestData.Envelope();
        var failure = new IOException("database failed");
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid())),
            DeadLetterStatus = _ => throw failure
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = static (_, _) => throw new InvalidOperationException("handler failed")
        };

        var exception = await Should.ThrowAsync<DurableOutputDeliveryDispatcher.DurableOutputDeliveryStoreException>(
            () => Dispatcher(store, handler, new FakeTimeProvider(Now), options: Limited(1))
                .ProcessOnceAsync().AsTask());

        exception.Operation.ShouldBe("dead-letter");
        exception.InnerException.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Dead_letter_logs_contain_metadata_without_payload_headers_or_failure_details()
    {
        var envelope = DurableOutputTestData.Envelope();
        var logger = new RecordingLogger<DurableOutputDeliveryDispatcher>();
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                Lease(request, envelope, Guid.NewGuid()))
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = static (_, _) => throw new InvalidOperationException("private-failure-detail")
        };

        await Dispatcher(store, handler, new FakeTimeProvider(Now), logger, Limited(1))
            .ProcessOnceAsync();

        var rendered = string.Join("\n", logger.Entries.Select(static entry => entry.Message));
        rendered.ShouldContain(envelope.MessageId.Value);
        rendered.ShouldContain(envelope.Address.Value);
        rendered.ShouldContain(nameof(DurableOutputDeadLetterReason.HandlerFailure));
        rendered.ShouldNotContain("private-failure-detail");
        rendered.ShouldNotContain(envelope.Payload.GetRawText());
        foreach (var header in envelope.Headers)
        {
            rendered.ShouldNotContain(header.Key);
            rendered.ShouldNotContain(header.Value);
        }
    }

    [Fact]
    public async Task Hosted_dead_letter_store_failure_logs_waits_idle_delay_and_recovers_after_expiry()
    {
        var clock = new TrackingFakeTimeProvider(Now);
        var envelope = DurableOutputTestData.Envelope();
        var leaseCalls = 0;
        var deadLetterCalls = 0;
        var recovered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ScriptedDeliveryStore
        {
            OnLease = (request, _) => Interlocked.Increment(ref leaseCalls) switch
            {
                1 => ValueTask.FromResult<DurableOutputDeliveryLease?>(
                    Lease(request, envelope, Guid.NewGuid())),
                2 when request.Now >= Now + Options.LeaseDuration =>
                    ValueTask.FromResult<DurableOutputDeliveryLease?>(
                        Lease(request, envelope, Guid.NewGuid(), attempt: 2)),
                _ => ValueTask.FromResult<DurableOutputDeliveryLease?>(null)
            },
            DeadLetterStatus = deadLetter => Interlocked.Increment(ref deadLetterCalls) == 1
                ? throw new IOException("private-store-failure")
                : CompleteRecovery(deadLetter)
        };
        var handler = new ScriptedDeliveryHandler
        {
            OnDeliver = static (_, _) => throw new InvalidOperationException("handler failed")
        };
        var logger = new RecordingLogger<DurableOutputDeliveryDispatcher>();
        var dispatcher = Dispatcher(store, handler, clock, logger, Limited(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await dispatcher.StartAsync(CancellationToken.None);
        await clock.TimerCreated.Task.WaitAsync(timeout.Token);

        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error &&
            entry.Message.Contains("store operation dead-letter failed", StringComparison.Ordinal) &&
            entry.Message.Contains(typeof(IOException).FullName!, StringComparison.Ordinal) &&
            entry.Exception == null);
        clock.DueTimes.ShouldHaveSingleItem().ShouldBe(Options.IdleDelay);
        clock.Advance(Options.LeaseDuration);
        await recovered.Task.WaitAsync(timeout.Token);

        deadLetterCalls.ShouldBe(2);
        store.DeadLetters.Count.ShouldBe(2);
        store.DeadLetters.Select(static item => item.Key)
            .ShouldAllBe(key => key == envelope.Key);
        string.Join("\n", logger.Entries.Select(static entry => entry.Message))
            .ShouldNotContain("private-store-failure");
        await dispatcher.StopAsync(timeout.Token);

        DurableOutputDeliveryTransitionResult CompleteRecovery(
            DurableOutputDeliveryDeadLetter deadLetter)
        {
            recovered.TrySetResult();
            return new DurableOutputDeliveryTransitionResult(
                deadLetter.Key,
                DurableOutputDeliveryTransitionStatus.Applied);
        }
    }

    private static DurableOutputDeliveryDispatcher Dispatcher(
        IDurableOutputDeliveryStore store,
        IDurableOutputDeliveryHandler handler,
        TimeProvider clock,
        ILogger<DurableOutputDeliveryDispatcher>? logger = null,
        DurableOutputDeliveryOptions? options = null)
        => new(
            store,
            handler,
            options ?? Options,
            clock,
            logger ?? new RecordingLogger<DurableOutputDeliveryDispatcher>());

    private static ScriptedDeliveryHandler CancellableHandler(
        TaskCompletionSource entered,
        TaskCompletionSource observed)
        => new()
        {
            OnDeliver = async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    observed.TrySetResult();
                }
            }
        };

    private static void AssertUnsettled(ScriptedDeliveryStore store)
    {
        store.Completions.ShouldBeEmpty();
        store.Retries.ShouldBeEmpty();
        store.DeadLetters.ShouldBeEmpty();
    }

    private static DurableOutputDeliveryLease Lease(
        DurableOutputDeliveryLeaseRequest request,
        DurableOutputEnvelope envelope,
        Guid token,
        int attempt = 1)
        => new(
            envelope,
            token,
            request.OwnerId,
            request.Now,
            request.LeaseUntil,
            attempt);

    private sealed class ScriptedDeliveryStore : IDurableOutputDeliveryStore
    {
        private int _leaseCalls;

        public ConcurrentQueue<DurableOutputDeliveryLeaseRequest> LeaseRequests { get; } = new();

        public ConcurrentQueue<DurableOutputDeliveryLeaseRenewal> Renewals { get; } = new();

        public ConcurrentQueue<DurableOutputDeliveryTransition> Completions { get; } = new();

        public ConcurrentQueue<DurableOutputDeliveryRetry> Retries { get; } = new();

        public ConcurrentQueue<DurableOutputDeliveryDeadLetter> DeadLetters { get; } = new();

        public TaskCompletionSource SecondLeaseObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<DurableOutputDeliveryLeaseRequest, CancellationToken,
            ValueTask<DurableOutputDeliveryLease?>> OnLease
        { get; init; } =
            static (_, _) => ValueTask.FromResult<DurableOutputDeliveryLease?>(null);

        public Func<DurableOutputDeliveryLeaseRenewal, CancellationToken,
            ValueTask<DurableOutputDeliveryTransitionResult>> OnRenew
        { get; init; } = static (renewal, _) =>
            ValueTask.FromResult(new DurableOutputDeliveryTransitionResult(
                renewal.Key,
                DurableOutputDeliveryTransitionStatus.Applied));

        public Func<DurableOutputDeliveryTransition, DurableOutputDeliveryTransitionResult>
            CompleteStatus
        { get; init; } = static transition =>
                new DurableOutputDeliveryTransitionResult(
                    transition.Key,
                    DurableOutputDeliveryTransitionStatus.Applied);

        public Func<DurableOutputDeliveryRetry, DurableOutputDeliveryTransitionResult>
            RetryStatus
        { get; init; } = static retry =>
                new DurableOutputDeliveryTransitionResult(
                    retry.Key,
                    DurableOutputDeliveryTransitionStatus.Applied);

        public Func<DurableOutputDeliveryDeadLetter, DurableOutputDeliveryTransitionResult>
            DeadLetterStatus
        { get; init; } = static deadLetter =>
                new DurableOutputDeliveryTransitionResult(
                    deadLetter.Key,
                    DurableOutputDeliveryTransitionStatus.Applied);

        public ValueTask<DurableOutputDeliveryLease?> TryLeaseAsync(
            DurableOutputDeliveryLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            LeaseRequests.Enqueue(request);
            if (Interlocked.Increment(ref _leaseCalls) == 2)
                SecondLeaseObserved.TrySetResult();
            return OnLease(request, cancellationToken);
        }

        public ValueTask<DurableOutputDeliveryTransitionResult> RenewLeaseAsync(
            DurableOutputDeliveryLeaseRenewal renewal,
            CancellationToken cancellationToken = default)
        {
            Renewals.Enqueue(renewal);
            return OnRenew(renewal, cancellationToken);
        }

        public ValueTask<DurableOutputDeliveryTransitionResult> CompleteAsync(
            DurableOutputDeliveryTransition transition,
            CancellationToken cancellationToken = default)
        {
            Completions.Enqueue(transition);
            return ValueTask.FromResult(CompleteStatus(transition));
        }

        public ValueTask<DurableOutputDeliveryTransitionResult> RetryAsync(
            DurableOutputDeliveryRetry retry,
            CancellationToken cancellationToken = default)
        {
            Retries.Enqueue(retry);
            return ValueTask.FromResult(RetryStatus(retry));
        }

        public ValueTask<DurableOutputDeliveryTransitionResult> DeadLetterAsync(
            DurableOutputDeliveryDeadLetter deadLetter,
            CancellationToken cancellationToken = default)
        {
            DeadLetters.Enqueue(deadLetter);
            return ValueTask.FromResult(DeadLetterStatus(deadLetter));
        }
    }

    private sealed class ScriptedDeliveryHandler : IDurableOutputDeliveryHandler
    {
        public ConcurrentQueue<DurableOutputEnvelope> Envelopes { get; } = new();

        public Func<DurableOutputEnvelope, CancellationToken, ValueTask> OnDeliver { get; init; } =
            static (_, _) => ValueTask.CompletedTask;

        public ValueTask DeliverAsync(
            DurableOutputEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Envelopes.Enqueue(envelope);
            return OnDeliver(envelope, cancellationToken);
        }
    }

    private sealed class TrackingFakeTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
    {
        public ConcurrentQueue<TimeSpan> DueTimes { get; } = new();

        public TaskCompletionSource TimerCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            DueTimes.Enqueue(dueTime);
            TimerCreated.TrySetResult();
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<Entry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Enqueue(new Entry(logLevel, formatter(state, exception), exception));

        public sealed record Entry(LogLevel Level, string Message, Exception? Exception);
    }

    public enum LeaseTimeMismatch
    {
        LeasedAtInstant,
        LeasedAtOffset,
        LeaseUntilInstant,
        LeaseUntilOffset
    }
}
