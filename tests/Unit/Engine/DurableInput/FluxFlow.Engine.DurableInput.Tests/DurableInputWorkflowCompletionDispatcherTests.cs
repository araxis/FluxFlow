using FluxFlow.Composition.Addressing;
using FluxFlow.Engine.DurableInput;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableInputWorkflowCompletionDispatcherTests
{
    [Fact]
    public async Task Engine_accepted_mode_keeps_batching_and_never_uses_completion_capabilities()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = new DurableInputTestStore();
        var first = DurableInputTestData.Envelope(
            value: "first",
            messageId: new MessageId("accepted-first"));
        var second = DurableInputTestData.Envelope(
            value: "second",
            enqueuedAt: DurableInputTestData.Now,
            messageId: new MessageId("accepted-second"));
        await store.EnqueueAsync(first);
        await store.EnqueueAsync(second);
        var source = new DurableInputCompletionTestSource
        {
            SubscribeException = new InvalidOperationException("must not be used")
        };
        var options = new DurableInputOptions(
            batchSize: 2,
            leaseDuration: TimeSpan.FromSeconds(30),
            pollInterval: TimeSpan.FromMilliseconds(250),
            retryDelay: TimeSpan.FromSeconds(1),
            storeFailureDelay: TimeSpan.FromSeconds(2),
            maxDeliveryAttempts: 10);
        var dispatcher = Dispatcher(store, host.Application, Clock(), source, options);

        (await dispatcher.ProcessOnceAsync()).ShouldBeTrue();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);
        await host.Recorder.WaitAsync(timeout.Token);

        store.LeaseRequests.ShouldHaveSingleItem().MaxCount.ShouldBe(2);
        store.DeliveredTransitions.Count.ShouldBe(2);
        store.Renewals.ShouldBeEmpty();
        source.SubscribeCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_envelope_is_rejected_before_completion_subscription()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = new DurableInputTestStore();
        var envelope = new DurableInputEnvelope(
            DurableInputTestData.Input,
            "missing-v1",
            isError: false,
            DurableInputTestData.Envelope().Payload,
            error: null,
            new MessageId("unknown-contract-completion"),
            new TraceId("unknown-contract-trace"),
            DurableInputTestData.Now,
            DurableInputTestData.Now);
        await store.EnqueueAsync(envelope);
        var source = new DurableInputCompletionTestSource();

        await Dispatcher(store, host.Application, Clock(), source).ProcessOnceAsync();

        source.SubscribeCalls.ShouldBe(0);
        store.DeadLetters.ShouldHaveSingleItem().Failure.Kind
            .ShouldBe(DurableInputFailureKind.UnknownContract);
        host.Recorder.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Subscription_precedes_send_and_explicit_success_is_the_only_delivery_boundary()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = await StoreWithEnvelopeAsync();
        var source = new DurableInputCompletionTestSource { BlockSubscription = true };
        var dispatcher = Dispatcher(store, host.Application, clock, source);

        var processing = dispatcher.ProcessOnceAsync().AsTask();
        using var timeout = Timeout();
        await source.Subscribed.Task.WaitAsync(timeout.Token);

        host.Recorder.Messages.ShouldBeEmpty();
        store.DeliveredTransitions.ShouldBeEmpty();
        store.Get(DurableInputTestData.Envelope().Key).State.ShouldBe(DurableInputState.Leased);

        source.ContinueSubscription();
        var received = await host.Recorder.WaitAsync(timeout.Token);

        received.Message.MessageId.ShouldBe(DurableInputTestData.Envelope().MessageId);
        source.Leases.ShouldHaveSingleItem().Envelope.Key
            .ShouldBe(DurableInputTestData.Envelope().Key);
        store.DeliveredTransitions.ShouldBeEmpty();
        store.Get(DurableInputTestData.Envelope().Key).State.ShouldBe(DurableInputState.Leased);

        source.Subscription!.Complete();
        (await processing.WaitAsync(timeout.Token)).ShouldBeTrue();

        store.Get(DurableInputTestData.Envelope().Key).State.ShouldBe(DurableInputState.Delivered);
        store.DeliveredTransitions.ShouldHaveSingleItem().Key
            .ShouldBe(DurableInputTestData.Envelope().Key);
        source.Subscription.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Workflow_completion_leases_only_one_record_even_when_batch_size_is_larger()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = new DurableInputTestStore();
        var first = DurableInputTestData.Envelope(
            value: "first",
            messageId: new MessageId("completion-first"));
        var second = DurableInputTestData.Envelope(
            value: "second",
            enqueuedAt: DurableInputTestData.Now.AddSeconds(1),
            messageId: new MessageId("completion-second"));
        await store.EnqueueAsync(first);
        await store.EnqueueAsync(second);
        var source = new DurableInputCompletionTestSource();
        var processing = Dispatcher(store, host.Application, Clock(), source)
            .ProcessOnceAsync().AsTask();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);

        store.LeaseRequests.ShouldHaveSingleItem().MaxCount.ShouldBe(1);
        store.Get(first.Key).State.ShouldBe(DurableInputState.Leased);
        store.Get(second.Key).State.ShouldBe(DurableInputState.Pending);

        source.Subscription!.Complete();
        (await processing.WaitAsync(timeout.Token)).ShouldBeTrue();

        store.Get(first.Key).State.ShouldBe(DurableInputState.Delivered);
        store.Get(second.Key).State.ShouldBe(DurableInputState.Pending);
        host.Recorder.Messages.ShouldHaveSingleItem().Message.MessageId.ShouldBe(first.MessageId);
    }

    [Fact]
    public async Task Explicit_completion_failure_releases_with_the_host_owned_stable_description()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = await StoreWithEnvelopeAsync();
        var source = new DurableInputCompletionTestSource();
        var logger = new RecordingLogger<DurableInputDispatcher>();
        var processing = Dispatcher(store, host.Application, Clock(), source, logger: logger)
            .ProcessOnceAsync().AsTask();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);

        source.Subscription!.Fail("The terminal operation rejected the message.");
        await processing.WaitAsync(timeout.Token);

        var release = store.Releases.ShouldHaveSingleItem();
        release.Failure.Kind.ShouldBe(DurableInputFailureKind.WorkflowCompletionFailed);
        release.Failure.Description.ShouldBe("The terminal operation rejected the message.");
        store.Get(release.Key).State.ShouldBe(DurableInputState.Pending);
        logger.Messages.ShouldNotContain(message =>
            message.Contains("terminal operation rejected", StringComparison.OrdinalIgnoreCase));
        source.Subscription.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Completion_failure_at_the_attempt_limit_dead_letters_through_existing_policy()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = await StoreWithEnvelopeAsync();
        var source = new DurableInputCompletionTestSource();
        var processing = Dispatcher(
                store,
                host.Application,
                Clock(),
                source,
                options: WorkflowOptions(maxDeliveryAttempts: 1))
            .ProcessOnceAsync().AsTask();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);

        source.Subscription!.Fail("The terminal operation failed.");
        await processing.WaitAsync(timeout.Token);

        store.Releases.ShouldBeEmpty();
        var deadLetter = store.DeadLetters.ShouldHaveSingleItem();
        deadLetter.Failure.Kind.ShouldBe(DurableInputFailureKind.MaximumAttemptsExceeded);
        deadLetter.Failure.Description.ShouldContain(
            DurableInputFailureKind.WorkflowCompletionFailed.ToString());
        store.Get(deadLetter.Key).State.ShouldBe(DurableInputState.DeadLettered);
    }

    [Theory]
    [InlineData(SubscriptionFailure.Throw)]
    [InlineData(SubscriptionFailure.NullSubscription)]
    [InlineData(SubscriptionFailure.NullCompletionTask)]
    [InlineData(SubscriptionFailure.ThrowingCompletionGetter)]
    public async Task Invalid_completion_subscription_retries_without_sending(
        SubscriptionFailure failure)
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = await StoreWithEnvelopeAsync();
        var source = new DurableInputCompletionTestSource
        {
            SubscribeException = failure == SubscriptionFailure.Throw
                ? new InvalidOperationException("private source detail")
                : null,
            ReturnNullSubscription = failure == SubscriptionFailure.NullSubscription,
            ReturnNullCompletion = failure == SubscriptionFailure.NullCompletionTask,
            CompletionGetterException = failure == SubscriptionFailure.ThrowingCompletionGetter
                ? new InvalidOperationException("private completion getter detail")
                : null
        };
        var logger = new RecordingLogger<DurableInputDispatcher>();

        await Dispatcher(store, host.Application, Clock(), source, logger: logger)
            .ProcessOnceAsync();

        host.Recorder.Messages.ShouldBeEmpty();
        var release = store.Releases.ShouldHaveSingleItem();
        release.Failure.Kind.ShouldBe(DurableInputFailureKind.CompletionSourceUnavailable);
        store.Get(release.Key).State.ShouldBe(DurableInputState.Pending);
        if (failure is SubscriptionFailure.NullCompletionTask or
            SubscriptionFailure.ThrowingCompletionGetter)
        {
            source.Subscription!.DisposeCalls.ShouldBe(1);
        }
        else
        {
            source.Subscription.ShouldBeNull();
        }
        logger.Messages.ShouldNotContain(message =>
            message.Contains("private source detail", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(CompletionFailure.Fault, DurableInputFailureKind.WorkflowCompletionFailed)]
    [InlineData(CompletionFailure.Cancelled, DurableInputFailureKind.WorkflowCompletionFailed)]
    [InlineData(CompletionFailure.NullResult, DurableInputFailureKind.CompletionSourceUnavailable)]
    public async Task Invalid_completion_result_retries_without_marking_delivered(
        CompletionFailure failure,
        DurableInputFailureKind expectedKind)
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = await StoreWithEnvelopeAsync();
        var source = new DurableInputCompletionTestSource();
        var logger = new RecordingLogger<DurableInputDispatcher>();
        var processing = Dispatcher(store, host.Application, Clock(), source, logger: logger)
            .ProcessOnceAsync().AsTask();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);

        if (failure == CompletionFailure.Fault)
            source.Subscription!.Fault(new InvalidOperationException("private task detail"));
        else if (failure == CompletionFailure.Cancelled)
            source.Subscription!.Cancel();
        else
            source.Subscription!.ReturnNull();
        await processing.WaitAsync(timeout.Token);

        store.DeliveredTransitions.ShouldBeEmpty();
        store.Releases.ShouldHaveSingleItem().Failure.Kind.ShouldBe(expectedKind);
        source.Subscription.DisposeCalls.ShouldBe(1);
        logger.Messages.ShouldNotContain(message =>
            message.Contains("private task detail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Timeout_releases_the_current_lease_using_the_injected_clock()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = await StoreWithEnvelopeAsync();
        var source = new DurableInputCompletionTestSource();
        var processing = Dispatcher(
                store,
                host.Application,
                clock,
                source,
                options: WorkflowOptions(
                    leaseDuration: TimeSpan.FromMinutes(1),
                    timeout: TimeSpan.FromSeconds(5),
                    renewalInterval: TimeSpan.FromSeconds(10)))
            .ProcessOnceAsync().AsTask();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);

        clock.Advance(TimeSpan.FromSeconds(5));
        await processing.WaitAsync(timeout.Token);

        var release = store.Releases.ShouldHaveSingleItem();
        release.ReleasedAt.ShouldBe(DurableInputTestData.Now.AddSeconds(5));
        release.Failure.Kind.ShouldBe(DurableInputFailureKind.WorkflowCompletionTimedOut);
        store.Renewals.ShouldBeEmpty();
        store.DeliveredTransitions.ShouldBeEmpty();
        source.Subscription!.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Waiting_renews_the_exact_lease_then_successfully_settles_it()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = await StoreWithEnvelopeAsync();
        var source = new DurableInputCompletionTestSource();
        var processing = Dispatcher(
                store,
                host.Application,
                clock,
                source,
                options: WorkflowOptions(timeout: System.Threading.Timeout.InfiniteTimeSpan))
            .ProcessOnceAsync().AsTask();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);

        clock.Advance(TimeSpan.FromSeconds(10));
        await store.RenewalObserved.Task.WaitAsync(timeout.Token);

        var renewal = store.Renewals.ShouldHaveSingleItem();
        var lease = source.Leases.ShouldHaveSingleItem();
        renewal.Key.ShouldBe(lease.Envelope.Key);
        renewal.LeaseToken.ShouldBe(lease.LeaseToken);
        renewal.RenewedAt.ShouldBe(DurableInputTestData.Now.AddSeconds(10));
        renewal.LeaseUntil.ShouldBe(DurableInputTestData.Now.AddSeconds(40));
        store.Get(renewal.Key).Attempt.ShouldBe(1);
        store.Get(renewal.Key).LeaseUntil.ShouldBe(renewal.LeaseUntil);

        source.Subscription!.Complete();
        await processing.WaitAsync(timeout.Token);

        store.Get(renewal.Key).State.ShouldBe(DurableInputState.Delivered);
        store.DeliveredTransitions.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Completion_available_before_wait_settles_without_a_timer_or_renewal()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = await StoreWithEnvelopeAsync();
        var source = new DurableInputCompletionTestSource { CompleteOnSubscribe = true };

        await Dispatcher(store, host.Application, Clock(), source).ProcessOnceAsync();

        source.SubscribeCalls.ShouldBe(1);
        store.Renewals.ShouldBeEmpty();
        store.DeliveredTransitions.ShouldHaveSingleItem();
        store.Get(DurableInputTestData.Envelope().Key).State.ShouldBe(DurableInputState.Delivered);
        source.Subscription!.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Lost_renewal_stops_waiting_without_using_the_stale_lease_again()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = await StoreWithEnvelopeAsync();
        store.LoseNextRenewal = true;
        var source = new DurableInputCompletionTestSource();
        var processing = Dispatcher(
                store,
                host.Application,
                clock,
                source,
                options: WorkflowOptions(timeout: System.Threading.Timeout.InfiniteTimeSpan))
            .ProcessOnceAsync().AsTask();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);

        clock.Advance(TimeSpan.FromSeconds(10));
        await processing.WaitAsync(timeout.Token);
        source.Subscription!.Complete();

        store.Renewals.ShouldHaveSingleItem();
        store.DeliveredTransitions.ShouldBeEmpty();
        store.Releases.ShouldBeEmpty();
        store.DeadLetters.ShouldBeEmpty();
        store.Get(DurableInputTestData.Envelope().Key).State.ShouldBe(DurableInputState.Leased);
        source.Subscription.DisposeCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(DurableInputTransitionStatus.LeaseLost)]
    [InlineData(DurableInputTransitionStatus.NotFound)]
    [InlineData(DurableInputTransitionStatus.InvalidState)]
    public async Task Any_non_applied_renewal_stops_without_settlement(
        DurableInputTransitionStatus status)
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = await StoreWithEnvelopeAsync();
        store.ForcedRenewalStatus = status;
        var source = new DurableInputCompletionTestSource();
        var processing = Dispatcher(
                store,
                host.Application,
                clock,
                source,
                options: WorkflowOptions(timeout: System.Threading.Timeout.InfiniteTimeSpan))
            .ProcessOnceAsync().AsTask();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);

        clock.Advance(TimeSpan.FromSeconds(10));
        await processing.WaitAsync(timeout.Token);
        source.Subscription!.Complete();

        store.Renewals.ShouldHaveSingleItem();
        store.DeliveredTransitions.ShouldBeEmpty();
        store.Releases.ShouldBeEmpty();
        store.DeadLetters.ShouldBeEmpty();
        source.Subscription.DisposeCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(RenewalStoreFailure.Throw)]
    [InlineData(RenewalStoreFailure.NullResult)]
    [InlineData(RenewalStoreFailure.WrongKey)]
    public async Task Invalid_renewal_store_result_uses_store_failure_path_without_settlement(
        RenewalStoreFailure failure)
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = await StoreWithEnvelopeAsync();
        store.RenewalException = failure == RenewalStoreFailure.Throw
            ? new IOException("private store detail")
            : null;
        store.ReturnNullRenewalResult = failure == RenewalStoreFailure.NullResult;
        if (failure == RenewalStoreFailure.WrongKey)
        {
            store.ForcedRenewalStatus = DurableInputTransitionStatus.Applied;
            store.RenewalResultKey = DurableInputTestData.Envelope(
                messageId: new MessageId("different-renewal-result")).Key;
        }
        var source = new DurableInputCompletionTestSource();
        var processing = Dispatcher(
                store,
                host.Application,
                clock,
                source,
                options: WorkflowOptions(timeout: System.Threading.Timeout.InfiniteTimeSpan))
            .ProcessOnceAsync().AsTask();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);

        clock.Advance(TimeSpan.FromSeconds(10));
        var exception = await Should.ThrowAsync<Exception>(async () =>
            await processing.WaitAsync(timeout.Token));

        exception.Message.ShouldContain("renew");
        store.DeliveredTransitions.ShouldBeEmpty();
        store.Releases.ShouldBeEmpty();
        store.DeadLetters.ShouldBeEmpty();
        source.Subscription!.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Rejected_send_uses_existing_retry_reason_and_disposes_subscription()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = await StoreWithEnvelopeAsync();
        var source = new DurableInputCompletionTestSource();

        await Dispatcher(
                store,
                host.Application,
                Clock(),
                source,
                contract: new FixedSendContract(PortSendStatus.Unavailable))
            .ProcessOnceAsync();

        store.Releases.ShouldHaveSingleItem().Failure.Kind
            .ShouldBe(DurableInputFailureKind.InputUnavailable);
        store.Renewals.ShouldBeEmpty();
        store.DeliveredTransitions.ShouldBeEmpty();
        source.Subscription!.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_leaves_the_lease_unsettled_and_disposes_the_subscription()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = await StoreWithEnvelopeAsync();
        var source = new DurableInputCompletionTestSource();
        using var cancellation = new CancellationTokenSource();
        var processing = Dispatcher(store, host.Application, Clock(), source)
            .ProcessOnceAsync(cancellation.Token).AsTask();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);

        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await processing.WaitAsync(timeout.Token));

        store.DeliveredTransitions.ShouldBeEmpty();
        store.Releases.ShouldBeEmpty();
        store.DeadLetters.ShouldBeEmpty();
        store.Get(DurableInputTestData.Envelope().Key).State.ShouldBe(DurableInputState.Leased);
        source.Subscription!.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Subscription_disposal_failure_is_safe_and_does_not_reverse_delivery()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = await StoreWithEnvelopeAsync();
        var source = new DurableInputCompletionTestSource
        {
            DisposeException = new InvalidOperationException("secret disposal detail")
        };
        var logger = new RecordingLogger<DurableInputDispatcher>();
        var processing = Dispatcher(store, host.Application, Clock(), source, logger: logger)
            .ProcessOnceAsync().AsTask();
        using var timeout = Timeout();
        await host.Recorder.WaitAsync(timeout.Token);

        source.Subscription!.Complete();
        await processing.WaitAsync(timeout.Token);

        store.Get(DurableInputTestData.Envelope().Key).State.ShouldBe(DurableInputState.Delivered);
        source.Subscription.DisposeCalls.ShouldBe(1);
        logger.Messages.ShouldContain(message => message.Contains(
            typeof(InvalidOperationException).FullName!,
            StringComparison.Ordinal));
        logger.Messages.ShouldNotContain(message =>
            message.Contains("secret disposal detail", StringComparison.Ordinal));
    }

    private static async ValueTask<DurableInputTestStore> StoreWithEnvelopeAsync()
    {
        var store = new DurableInputTestStore();
        await store.EnqueueAsync(DurableInputTestData.Envelope());
        return store;
    }

    private static DurableInputDispatcher Dispatcher(
        DurableInputTestStore store,
        FluxFlowApplication application,
        TimeProvider clock,
        DurableInputCompletionTestSource source,
        DurableInputOptions? options = null,
        IDurableInputContract? contract = null,
        ILogger<DurableInputDispatcher>? logger = null)
        => new(
            store,
            new DurableInputContractRegistry(
            [
                contract ?? new DurableInputContract<string>("text-v1", jsonTypeInfo: null)
            ]),
            application,
            options ?? WorkflowOptions(),
            clock,
            logger ?? NullLogger<DurableInputDispatcher>.Instance,
            source,
            store);

    private static DurableInputOptions WorkflowOptions(
        int maxDeliveryAttempts = 10,
        TimeSpan? leaseDuration = null,
        TimeSpan? timeout = null,
        TimeSpan? renewalInterval = null)
        => new(
            batchSize: 64,
            leaseDuration ?? TimeSpan.FromSeconds(30),
            pollInterval: TimeSpan.FromMilliseconds(250),
            retryDelay: TimeSpan.FromSeconds(1),
            storeFailureDelay: TimeSpan.FromSeconds(2),
            maxDeliveryAttempts,
            DurableInputAcknowledgementMode.WorkflowCompleted,
            timeout ?? TimeSpan.FromMinutes(5),
            renewalInterval ?? TimeSpan.FromSeconds(10));

    private static FakeTimeProvider Clock() => new(DurableInputTestData.Now);

    private static CancellationTokenSource Timeout() =>
        new(TimeSpan.FromSeconds(5));

    public enum SubscriptionFailure
    {
        Throw,
        NullSubscription,
        NullCompletionTask,
        ThrowingCompletionGetter
    }

    public enum CompletionFailure
    {
        Fault,
        Cancelled,
        NullResult
    }

    public enum RenewalStoreFailure
    {
        Throw,
        NullResult,
        WrongKey
    }

    private sealed class FixedSendContract(PortSendStatus status) : IDurableInputContract
    {
        public string Name => "text-v1";

        public Type PayloadType => typeof(string);

        public bool IsEquivalentTo(IDurableInputContract other) => ReferenceEquals(this, other);

        public DurableInputEnvelope CreateEnvelope<TMessage>(
            ApplicationAddress address,
            FlowMessage<TMessage> message,
            DateTimeOffset enqueuedAt)
            => throw new NotSupportedException();

        public ValueTask<PortSendResult> RestoreAndSendAsync(
            FluxFlowApplication application,
            DurableInputEnvelope envelope,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PortSendResult
            {
                Port = envelope.Address,
                Status = status
            });
        }
    }
}
