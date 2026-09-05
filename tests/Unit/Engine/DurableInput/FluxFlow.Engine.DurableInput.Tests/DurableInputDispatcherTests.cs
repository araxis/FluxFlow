using System.Text.Json;
using System.Collections.Concurrent;
using System.Text.Json.Serialization.Metadata;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.DurableInput;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableInputDispatcherTests
{
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("Orders", "Handler", "Output");
    private static readonly ApplicationAddress Signal =
        ApplicationAddress.WorkflowPort("Orders", "Handler", "Signal");
    private static readonly ApplicationAddress Missing =
        ApplicationAddress.WorkflowPort("Orders", "Missing", "Input");

    [Fact]
    public async Task Accepted_send_marks_delivered_and_restores_the_same_message_identity()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = new DurableInputTestStore();
        var envelope = DurableInputTestData.Envelope();
        await store.EnqueueAsync(envelope);
        var dispatcher = Dispatcher(store, host.Application, clock);

        var hadWork = await dispatcher.ProcessOnceAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await host.Recorder.WaitAsync(timeout.Token);

        hadWork.ShouldBeTrue();
        store.Get(envelope.Key).State.ShouldBe(DurableInputState.Delivered);
        var transition = store.DeliveredTransitions.ShouldHaveSingleItem();
        transition.Key.ShouldBe(envelope.Key);
        transition.OccurredAt.ShouldBe(DurableInputTestData.Now);
        received.Revision.ShouldBe("revision-a");
        received.Message.Value.ShouldBe("payload");
        received.Message.MessageId.ShouldBe(envelope.MessageId);
        received.Message.TraceId.ShouldBe(envelope.TraceId);
        received.Message.Timestamp.ShouldBe(envelope.Timestamp);
        received.Message.CorrelationId.ShouldBe(envelope.CorrelationId);
        received.Message.CausationId.ShouldBe(envelope.CausationId);
        received.Message.Headers.ShouldBe(envelope.Headers);
    }

    [Fact]
    public async Task Registered_payload_type_restores_complete_error_envelope_and_identity()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = new DurableInputTestStore();
        var contract = new DurableInputContract<string>("text-v1", jsonTypeInfo: null);
        var registry = new DurableInputContractRegistry([contract]);
        var error = new FlowError(
            "order.invalid",
            "The order is invalid.",
            "validation",
            isTransient: true,
            details: JsonSerializer.SerializeToElement(new { field = "customerId" }));
        var message = FlowMessage.RestoreError<string>(
            error,
            new MessageId("error-message"),
            new TraceId("error-trace"),
            DurableInputTestData.Now.AddMinutes(-2),
            new CorrelationId("error-correlation"),
            new MessageId("error-cause"),
            new Dictionary<string, string> { ["tenant"] = "north" });
        var client = new DurableApplicationInputs(store, registry, clock);
        var result = await client.EnqueueAsync(DurableInputTestData.Input, message);
        var envelope = store.Get(result.Key).Envelope;

        await Dispatcher(store, host.Application, clock, contract: contract).ProcessOnceAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await host.Recorder.WaitAsync(timeout.Token);

        envelope.ContractName.ShouldBe("text-v1");
        envelope.IsError.ShouldBeTrue();
        envelope.Payload.ValueKind.ShouldBe(JsonValueKind.Null);
        envelope.Error!.Code.ShouldBe(error.Code);
        envelope.Error.Message.ShouldBe(error.Message);
        envelope.Error.Category.ShouldBe(error.Category);
        envelope.Error.IsTransient.ShouldBeTrue();
        envelope.Error.Details!.Value.GetProperty("field").GetString().ShouldBe("customerId");
        received.Message.IsError.ShouldBeTrue();
        received.Message.Error!.ShouldBe(error);
        received.Message.MessageId.ShouldBe(message.MessageId);
        received.Message.TraceId.ShouldBe(message.TraceId);
        received.Message.Timestamp.ShouldBe(message.Timestamp);
        received.Message.CorrelationId.ShouldBe(message.CorrelationId);
        received.Message.CausationId.ShouldBe(message.CausationId);
        received.Message.Headers.ShouldBe(message.Headers);
        store.Get(result.Key).State.ShouldBe(DurableInputState.Delivered);
    }

    [Fact]
    public async Task Json_type_info_registration_serializes_and_restores_through_the_typed_bridge()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = new DurableInputTestStore();
        var typeInfo = (JsonTypeInfo<string>)JsonSerializerOptions.Default.GetTypeInfo(typeof(string));
        var services = new ServiceCollection();
        services.AddSingleton<IDurableInputStore>(store);
        services.AddSingleton<TimeProvider>(clock);
        services.AddFluxFlowDurableInput();
        services.AddFluxFlowDurableInputContract("text-v1", typeInfo);
        using var provider = services.BuildServiceProvider();
        var message = FlowMessage.Restore(
            "typed-payload",
            new MessageId("typed-message"),
            new TraceId("typed-trace"),
            DurableInputTestData.Now.AddMinutes(-1));

        var result = await provider.GetRequiredService<DurableApplicationInputs>()
            .EnqueueAsync(DurableInputTestData.Input, message);
        var dispatcher = new DurableInputDispatcher(
            store,
            provider.GetRequiredService<DurableInputContractRegistry>(),
            host.Application,
            provider.GetRequiredService<DurableInputOptions>(),
            clock,
            NullLogger<DurableInputDispatcher>.Instance);
        await dispatcher.ProcessOnceAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await host.Recorder.WaitAsync(timeout.Token);

        store.Get(result.Key).Envelope.Payload.GetString().ShouldBe("typed-payload");
        received.Message.Value.ShouldBe("typed-payload");
        received.Message.MessageId.ShouldBe(message.MessageId);
        received.Message.TraceId.ShouldBe(message.TraceId);
        store.Get(result.Key).State.ShouldBe(DurableInputState.Delivered);
    }

    [Theory]
    [InlineData(PermanentCase.UnsupportedSchema, DurableInputFailureKind.UnsupportedSchemaVersion)]
    [InlineData(PermanentCase.UnknownContract, DurableInputFailureKind.UnknownContract)]
    [InlineData(PermanentCase.MalformedPayload, DurableInputFailureKind.DeserializationFailed)]
    [InlineData(PermanentCase.DeserializationFailure, DurableInputFailureKind.DeserializationFailed)]
    [InlineData(PermanentCase.OutputPort, DurableInputFailureKind.NotMessageInput)]
    [InlineData(PermanentCase.SignalPort, DurableInputFailureKind.NotMessageInput)]
    [InlineData(PermanentCase.PayloadTypeMismatch, DurableInputFailureKind.PayloadTypeMismatch)]
    public async Task Permanent_failures_dead_letter_immediately(
        PermanentCase scenario,
        DurableInputFailureKind expectedFailure)
    {
        var componentType = scenario switch
        {
            PermanentCase.SignalPort => "test.durable-signal",
            PermanentCase.PayloadTypeMismatch => "test.durable-integer",
            _ => "test.durable-string-a"
        };
        await using var host = await DurableInputTestApplication.CreateAsync(componentType);
        var clock = Clock();
        var store = new DurableInputTestStore();
        var envelope = scenario switch
        {
            PermanentCase.UnsupportedSchema => CreateEnvelope(schemaVersion: 2),
            PermanentCase.UnknownContract => CreateEnvelope(contractName: "missing-v1"),
            PermanentCase.MalformedPayload => CreateEnvelope(
                payload: JsonSerializer.SerializeToElement(new { unexpected = true })),
            PermanentCase.DeserializationFailure => CreateEnvelope(),
            PermanentCase.OutputPort => CreateEnvelope(address: Output),
            PermanentCase.SignalPort => CreateEnvelope(address: Signal),
            PermanentCase.PayloadTypeMismatch => CreateEnvelope(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        await store.EnqueueAsync(envelope);

        var contract = scenario == PermanentCase.DeserializationFailure
            ? new ThrowingRestoreContract()
            : null;
        await Dispatcher(store, host.Application, clock, contract: contract).ProcessOnceAsync();

        var deadLetter = store.DeadLetters.ShouldHaveSingleItem();
        deadLetter.Key.ShouldBe(envelope.Key);
        deadLetter.DeadLetteredAt.ShouldBe(DurableInputTestData.Now);
        deadLetter.Failure.Kind.ShouldBe(expectedFailure);
        store.Releases.ShouldBeEmpty();
        store.Get(envelope.Key).State.ShouldBe(DurableInputState.DeadLettered);
    }

    [Fact]
    public async Task Missing_address_retries_then_dead_letters_at_the_attempt_limit()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var firstStore = new DurableInputTestStore();
        var first = CreateEnvelope(address: Missing, messageId: new MessageId("missing-first"));
        await firstStore.EnqueueAsync(first);

        await Dispatcher(firstStore, host.Application, clock).ProcessOnceAsync();

        var release = firstStore.Releases.ShouldHaveSingleItem();
        release.Failure.Kind.ShouldBe(DurableInputFailureKind.InputAddressMissing);
        release.ReleasedAt.ShouldBe(DurableInputTestData.Now);
        release.NextAttemptAt.ShouldBe(DurableInputTestData.Now.AddSeconds(1));
        firstStore.DeadLetters.ShouldBeEmpty();
        firstStore.Get(first.Key).State.ShouldBe(DurableInputState.Pending);

        var finalStore = new DurableInputTestStore();
        var final = CreateEnvelope(address: Missing, messageId: new MessageId("missing-final"));
        await finalStore.EnqueueAsync(final);
        await RaiseAttemptAsync(finalStore, final.Key, attempts: 9);

        await Dispatcher(finalStore, host.Application, clock).ProcessOnceAsync();

        var deadLetter = finalStore.DeadLetters.ShouldHaveSingleItem();
        deadLetter.Failure.Kind.ShouldBe(DurableInputFailureKind.MaximumAttemptsExceeded);
        deadLetter.Failure.Description.ShouldContain(
            DurableInputFailureKind.InputAddressMissing.ToString());
        finalStore.Releases.Count.ShouldBe(9);
        finalStore.Get(final.Key).Attempt.ShouldBe(10);
        finalStore.Get(final.Key).State.ShouldBe(DurableInputState.DeadLettered);
    }

    [Fact]
    public async Task Unavailable_application_is_released_for_retry()
    {
        await using var host = await DurableInputTestApplication.CreateAsync(start: false);
        var clock = Clock();
        var store = new DurableInputTestStore();
        var envelope = CreateEnvelope();
        await store.EnqueueAsync(envelope);

        await Dispatcher(store, host.Application, clock).ProcessOnceAsync();

        var release = store.Releases.ShouldHaveSingleItem();
        release.Failure.Kind.ShouldBe(DurableInputFailureKind.InputUnavailable);
        release.NextAttemptAt.ShouldBe(DurableInputTestData.Now.AddSeconds(1));
        store.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unavailable_send_status_is_released_for_retry()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = new DurableInputTestStore();
        var envelope = CreateEnvelope(messageId: new MessageId("unavailable-status"));
        await store.EnqueueAsync(envelope);

        await Dispatcher(
            store,
            host.Application,
            clock,
            contract: new FixedSendContract(PortSendStatus.Unavailable)).ProcessOnceAsync();

        var release = store.Releases.ShouldHaveSingleItem();
        release.Failure.Kind.ShouldBe(DurableInputFailureKind.InputUnavailable);
        release.ReleasedAt.ShouldBe(DurableInputTestData.Now);
        release.NextAttemptAt.ShouldBe(DurableInputTestData.Now.AddSeconds(1));
        store.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Completed_input_is_released_for_retry()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = new DurableInputTestStore();
        var envelope = CreateEnvelope();
        await store.EnqueueAsync(envelope);

        await Dispatcher(
            store,
            host.Application,
            clock,
            contract: new FixedSendContract(PortSendStatus.Completed)).ProcessOnceAsync();

        store.Releases.ShouldHaveSingleItem().Failure.Kind
            .ShouldBe(DurableInputFailureKind.InputCompleted);
        store.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Full_input_is_released_for_retry()
    {
        await using var host = await DurableInputTestApplication.CreateAsync(
            componentType: "test.durable-blocking",
            inputCapacity: 1);
        var blocking = host.Nodes.Blocking.ShouldNotBeNull();
        (await host.Application.Ports.SendAsync(
                DurableInputTestData.Input,
                FlowMessage.Create("blocking")))
            .Status.ShouldBe(PortSendStatus.Accepted);
        using var enteredTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await blocking.Entered.WaitAsync(enteredTimeout.Token);
        PortSendResult? last = null;
        for (var index = 0; index < 10; index++)
        {
            last = await host.Application.Ports.SendAsync(
                DurableInputTestData.Input,
                FlowMessage.Create($"fill-{index}"));
            if (last.Status == PortSendStatus.Full)
                break;
        }
        last.ShouldNotBeNull().Status.ShouldBe(PortSendStatus.Full);
        var clock = Clock();
        var store = new DurableInputTestStore();
        var envelope = CreateEnvelope();
        await store.EnqueueAsync(envelope);

        await Dispatcher(store, host.Application, clock).ProcessOnceAsync();

        store.Releases.ShouldHaveSingleItem().Failure.Kind
            .ShouldBe(DurableInputFailureKind.InputFull);
        store.DeadLetters.ShouldBeEmpty();
    }

    [Fact]
    public async Task Crash_after_acceptance_redelivers_the_same_message_after_lease_expiry()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = new DurableInputTestStore { LoseNextDeliveredTransition = true };
        var envelope = CreateEnvelope(messageId: new MessageId("crash-window"));
        await store.EnqueueAsync(envelope);
        var dispatcher = Dispatcher(store, host.Application, clock);

        await dispatcher.ProcessOnceAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.Recorder.WaitAsync(timeout.Token);
        var afterCrash = store.Get(envelope.Key);
        afterCrash.State.ShouldBe(DurableInputState.Leased);
        afterCrash.Attempt.ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(31));

        await dispatcher.ProcessOnceAsync();
        await host.Recorder.WaitAsync(timeout.Token);

        var messages = host.Recorder.Messages;
        messages.Count.ShouldBe(2);
        messages.Select(item => item.Message.MessageId)
            .ShouldBe([envelope.MessageId, envelope.MessageId]);
        messages.Select(item => item.Message.Value).ShouldBe(["payload", "payload"]);
        store.DeliveredTransitions.Count.ShouldBe(2);
        store.Get(envelope.Key).Attempt.ShouldBe(2);
        store.Get(envelope.Key).State.ShouldBe(DurableInputState.Delivered);
    }

    [Fact]
    public async Task Dispatch_resolves_the_current_revision_instead_of_persisting_a_revision()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var clock = Clock();
        var store = new DurableInputTestStore();
        var envelope = CreateEnvelope(messageId: new MessageId("revision-message"));
        await store.EnqueueAsync(envelope);

        var revision = await host.Application.ApplyAsync(
            "revision-b",
            DurableInputTestApplication.Definition("test.durable-string-b"));
        revision.IsApplied.ShouldBeTrue();
        await Dispatcher(store, host.Application, clock).ProcessOnceAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await host.Recorder.WaitAsync(timeout.Token);

        received.Revision.ShouldBe("revision-b");
        received.Message.MessageId.ShouldBe(envelope.MessageId);
        host.Recorder.Messages.ShouldHaveSingleItem();
        store.Get(envelope.Key).State.ShouldBe(DurableInputState.Delivered);
    }

    [Fact]
    public async Task Process_once_uses_bounded_lease_options_and_propagates_cancellation()
    {
        await using var host = await DurableInputTestApplication.CreateAsync(start: false);
        var store = new DurableInputTestStore();
        var clock = Clock();
        var options = new DurableInputOptions(
            batchSize: 3,
            leaseDuration: TimeSpan.FromSeconds(12),
            pollInterval: DurableInputOptions.Default.PollInterval,
            retryDelay: DurableInputOptions.Default.RetryDelay,
            storeFailureDelay: DurableInputOptions.Default.StoreFailureDelay,
            maxDeliveryAttempts: DurableInputOptions.Default.MaxDeliveryAttempts);
        var dispatcher = Dispatcher(store, host.Application, clock, options);

        (await dispatcher.ProcessOnceAsync()).ShouldBeFalse();
        var request = store.LeaseRequests.ShouldHaveSingleItem();
        request.MaxCount.ShouldBe(3);
        request.Now.ShouldBe(DurableInputTestData.Now);
        request.LeaseUntil.ShouldBe(DurableInputTestData.Now.AddSeconds(12));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await dispatcher.ProcessOnceAsync(cancellation.Token));
        store.LeaseRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Process_once_dispatches_a_bounded_batch_sequentially_in_lease_order()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = new DurableInputTestStore();
        var oldest = DurableInputTestData.Envelope(
            value: "oldest",
            enqueuedAt: DurableInputTestData.Now.AddMinutes(-2),
            messageId: new MessageId("batch-oldest"));
        var next = DurableInputTestData.Envelope(
            value: "next",
            enqueuedAt: DurableInputTestData.Now.AddMinutes(-1),
            messageId: new MessageId("batch-next"));
        await store.EnqueueAsync(next);
        await store.EnqueueAsync(oldest);

        await Dispatcher(
            store,
            host.Application,
            Clock(),
            new DurableInputOptions(
                batchSize: 2,
                leaseDuration: TimeSpan.FromSeconds(30),
                pollInterval: TimeSpan.FromMilliseconds(250),
                retryDelay: TimeSpan.FromSeconds(1),
                storeFailureDelay: TimeSpan.FromSeconds(2),
                maxDeliveryAttempts: 10)).ProcessOnceAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.Recorder.WaitAsync(timeout.Token);
        await host.Recorder.WaitAsync(timeout.Token);

        store.LeaseRequests.ShouldHaveSingleItem().MaxCount.ShouldBe(2);
        store.DeliveredTransitions.Select(item => item.Key)
            .ShouldBe([oldest.Key, next.Key]);
        host.Recorder.Messages.Select(item => item.Message.MessageId)
            .ShouldBe([oldest.MessageId, next.MessageId]);
        store.Get(oldest.Key).State.ShouldBe(DurableInputState.Delivered);
        store.Get(next.Key).State.ShouldBe(DurableInputState.Delivered);
    }

    [Fact]
    public async Task Hosted_lifecycle_stops_a_clock_wait_without_untracked_work()
    {
        await using var host = await DurableInputTestApplication.CreateAsync(start: false);
        var store = new DurableInputTestStore();
        var dispatcher = Dispatcher(store, host.Application, Clock());

        await dispatcher.StartAsync(CancellationToken.None);
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await store.LeaseObserved.Task.WaitAsync(observationTimeout.Token);
        await dispatcher.StopAsync(observationTimeout.Token);

        dispatcher.ExecuteTask.ShouldNotBeNull().IsCompleted.ShouldBeTrue();
        store.LeaseRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Store_cycle_failure_waits_for_store_failure_delay_without_a_hot_loop()
    {
        await using var host = await DurableInputTestApplication.CreateAsync(start: false);
        var clock = new TrackingFakeTimeProvider(DurableInputTestData.Now);
        var store = new DurableInputTestStore
        {
            LeaseException = new IOException("store unavailable")
        };
        var options = new DurableInputOptions(
            batchSize: 1,
            leaseDuration: TimeSpan.FromSeconds(30),
            pollInterval: TimeSpan.FromMilliseconds(100),
            retryDelay: TimeSpan.FromSeconds(1),
            storeFailureDelay: TimeSpan.FromSeconds(5),
            maxDeliveryAttempts: 10);
        var dispatcher = Dispatcher(store, host.Application, clock, options);

        await dispatcher.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await clock.TimerCreated.Task.WaitAsync(timeout.Token);

        store.LeaseCalls.ShouldBe(1);
        clock.DueTimes.ShouldHaveSingleItem().ShouldBe(options.StoreFailureDelay);
        clock.Advance(options.StoreFailureDelay - TimeSpan.FromMilliseconds(1));
        store.LeaseCalls.ShouldBe(1);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await store.SecondLeaseObserved.Task.WaitAsync(timeout.Token);

        store.LeaseCalls.ShouldBe(2);
        await dispatcher.StopAsync(timeout.Token);
        dispatcher.ExecuteTask.ShouldNotBeNull().IsCompleted.ShouldBeTrue();
        store.LeaseCalls.ShouldBe(2);
    }

    private static DurableInputDispatcher Dispatcher(
        IDurableInputStore store,
        FluxFlowApplication application,
        TimeProvider clock,
        DurableInputOptions? options = null,
        IDurableInputContract? contract = null)
        => new(
            store,
            new DurableInputContractRegistry(
            [
                contract ?? new DurableInputContract<string>("text-v1", jsonTypeInfo: null)
            ]),
            application,
            options ?? DurableInputOptions.Default,
            clock,
            NullLogger<DurableInputDispatcher>.Instance);

    private static FakeTimeProvider Clock() => new(DurableInputTestData.Now);

    private static DurableInputEnvelope CreateEnvelope(
        ApplicationAddress? address = null,
        string contractName = "text-v1",
        JsonElement? payload = null,
        MessageId? messageId = null,
        int schemaVersion = DurableInputEnvelope.CurrentSchemaVersion)
        => new(
            address ?? DurableInputTestData.Input,
            contractName,
            isError: false,
            payload ?? JsonSerializer.SerializeToElement("payload"),
            error: null,
            messageId ?? new MessageId("message-1"),
            new TraceId("trace-1"),
            DurableInputTestData.Now.AddMinutes(-1),
            DurableInputTestData.Now,
            new CorrelationId("order-1"),
            new MessageId("cause-1"),
            new Dictionary<string, string> { ["source"] = "test" },
            schemaVersion);

    private static async ValueTask RaiseAttemptAsync(
        DurableInputTestStore store,
        DurableInputKey key,
        int attempts)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var lease = (await store.LeaseAsync(new DurableInputLeaseRequest(
                "preparation",
                DurableInputTestData.Now,
                DurableInputTestData.Now.AddSeconds(30),
                1))).Single();
            lease.Envelope.Key.ShouldBe(key);
            lease.Attempt.ShouldBe(attempt);
            (await store.ReleaseAsync(new DurableInputRelease(
                key,
                lease.LeaseToken,
                DurableInputTestData.Now,
                DurableInputTestData.Now,
                new DurableInputFailure(
                    DurableInputFailureKind.InputAddressMissing,
                    "Preparation retry."))))
                .Status.ShouldBe(DurableInputTransitionStatus.Applied);
        }
    }

    public enum PermanentCase
    {
        UnsupportedSchema,
        UnknownContract,
        MalformedPayload,
        DeserializationFailure,
        OutputPort,
        SignalPort,
        PayloadTypeMismatch
    }

    private sealed class FixedSendContract(PortSendStatus status) : IDurableInputContract
    {
        public string Name => "text-v1";

        public Type PayloadType => typeof(string);

        public bool IsEquivalentTo(IDurableInputContract other)
            => ReferenceEquals(this, other);

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

    private sealed class ThrowingRestoreContract : IDurableInputContract
    {
        public string Name => "text-v1";

        public Type PayloadType => typeof(string);

        public bool IsEquivalentTo(IDurableInputContract other)
            => ReferenceEquals(this, other);

        public DurableInputEnvelope CreateEnvelope<TMessage>(
            ApplicationAddress address,
            FlowMessage<TMessage> message,
            DateTimeOffset enqueuedAt)
            => throw new NotSupportedException();

        public ValueTask<PortSendResult> RestoreAndSendAsync(
            FluxFlowApplication application,
            DurableInputEnvelope envelope,
            CancellationToken cancellationToken)
            => ValueTask.FromException<PortSendResult>(
                new NotSupportedException("The registered JSON metadata rejected the payload."));
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
}
