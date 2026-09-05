using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableInputInstrumentationTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Instruments_have_exact_names_types_units_and_meter()
    {
        using var probe = new TelemetryProbe();

        DurableInputInstrumentation.RecordLeaseAcquired();

        probe.Instruments.Keys.Order(StringComparer.Ordinal).ShouldBe(
        [
            "fluxflow.durable_input.lease.renewals",
            "fluxflow.durable_input.leases.acquired",
            "fluxflow.durable_input.messages",
            "fluxflow.durable_input.processing.duration",
            "fluxflow.durable_input.store.failures"
        ]);
        AssertInstrument<Counter<long>>(probe, "fluxflow.durable_input.leases.acquired", "{lease}");
        AssertInstrument<Counter<long>>(probe, "fluxflow.durable_input.messages", "{message}");
        AssertInstrument<Counter<long>>(probe, "fluxflow.durable_input.lease.renewals", "{renewal}");
        AssertInstrument<Counter<long>>(probe, "fluxflow.durable_input.store.failures", "{failure}");
        AssertInstrument<Histogram<double>>(probe, "fluxflow.durable_input.processing.duration", "ms");
    }

    [Theory]
    [InlineData(InputOutcome.Delivered, "delivered", null)]
    [InlineData(InputOutcome.Retry, "retry", nameof(DurableInputFailureKind.InputFull))]
    [InlineData(InputOutcome.DeadLetter, "dead_letter", nameof(DurableInputFailureKind.UnsupportedSchemaVersion))]
    public async Task Process_once_records_exact_outcome_duration_activity_and_store_calls(
        InputOutcome scenario,
        string expectedOutcome,
        string? expectedFailureKind)
    {
        var clock = new SteppingTimeProvider(DurableInputTestData.Now);
        var store = new DurableInputTestStore();
        var envelope = DurableInputTestData.Envelope(
            value: "private-payload",
            messageId: new MessageId("private-message"),
            schemaVersion: scenario == InputOutcome.DeadLetter
                ? DurableInputEnvelope.CurrentSchemaVersion + 1
                : DurableInputEnvelope.CurrentSchemaVersion);
        await store.EnqueueAsync(envelope);
        await using var host = await DurableInputTestApplication.CreateAsync();
        var contract = new FixedSendContract(
            scenario == InputOutcome.Retry ? PortSendStatus.Full : PortSendStatus.Accepted);
        using var probe = new TelemetryProbe();

        var processed = await Dispatcher(store, host.Application, clock, contract: contract)
            .ProcessOnceAsync();

        processed.ShouldBeTrue();
        store.LeaseCalls.ShouldBe(1);
        store.DeliveredTransitions.Count.ShouldBe(scenario == InputOutcome.Delivered ? 1 : 0);
        store.Releases.Count.ShouldBe(scenario == InputOutcome.Retry ? 1 : 0);
        store.DeadLetters.Count.ShouldBe(scenario == InputOutcome.DeadLetter ? 1 : 0);
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_input.leases.acquired",
            1,
            new Dictionary<string, object?>());
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_input.messages",
            1,
            expectedFailureKind is null
                ? new Dictionary<string, object?> { ["outcome"] = expectedOutcome }
                : new Dictionary<string, object?>
                {
                    ["outcome"] = expectedOutcome,
                    ["failure.kind"] = expectedFailureKind
                });
        AssertDoubleMeasurement(
            probe,
            "fluxflow.durable_input.processing.duration",
            10,
            new Dictionary<string, object?>());
        AssertInputActivity(probe, envelope, DurableInputAcknowledgementMode.EngineAccepted);
        AssertMetricPrivacy(probe, envelope);
    }

    [Fact]
    public async Task Rejected_delivery_transition_does_not_record_completed_outcome()
    {
        var store = new DurableInputTestStore { LoseNextDeliveredTransition = true };
        var envelope = DurableInputTestData.Envelope();
        await store.EnqueueAsync(envelope);
        await using var host = await DurableInputTestApplication.CreateAsync();
        using var probe = new TelemetryProbe();

        await Dispatcher(
                store,
                host.Application,
                new SteppingTimeProvider(DurableInputTestData.Now),
                contract: new FixedSendContract(PortSendStatus.Accepted))
            .ProcessOnceAsync();

        store.LeaseCalls.ShouldBe(1);
        store.DeliveredTransitions.ShouldHaveSingleItem();
        store.Get(envelope.Key).State.ShouldBe(DurableInputState.Leased);
        probe.MeasurementsFor("fluxflow.durable_input.messages").ShouldBeEmpty();
        probe.MeasurementsFor("fluxflow.durable_input.processing.duration").Count.ShouldBe(1);
        probe.StoppedActivities.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(true, "applied")]
    [InlineData(false, "rejected")]
    public async Task Workflow_completion_records_exact_renewal_result_and_closes_once(
        bool applied,
        string expectedResult)
    {
        var clock = new GatedFakeTimeProvider(DurableInputTestData.Now);
        var store = new DurableInputTestStore
        {
            ForcedRenewalStatus = applied
                ? DurableInputTransitionStatus.Applied
                : DurableInputTransitionStatus.LeaseLost
        };
        var envelope = DurableInputTestData.Envelope();
        await store.EnqueueAsync(envelope);
        await using var host = await DurableInputTestApplication.CreateAsync();
        var completion = new ControlledCompletionSource();
        var options = WorkflowOptions();
        using var probe = new TelemetryProbe();
        var dispatcher = new DurableInputDispatcher(
            store,
            new DurableInputContractRegistry([new FixedSendContract(PortSendStatus.Accepted)]),
            host.Application,
            options,
            clock,
            NullLogger<DurableInputDispatcher>.Instance,
            completion,
            store);

        var execution = dispatcher.ProcessOnceAsync().AsTask();
        await completion.Subscribed.Task.WaitAsync(WaitTimeout);
        await clock.TimerCreated.Task.WaitAsync(WaitTimeout);
        clock.Advance(options.LeaseRenewalInterval);
        await store.RenewalObserved.Task.WaitAsync(WaitTimeout);
        if (applied)
            completion.Complete();
        (await execution.WaitAsync(WaitTimeout)).ShouldBeTrue();

        store.LeaseCalls.ShouldBe(1);
        store.Renewals.ShouldHaveSingleItem();
        store.DeliveredTransitions.Count.ShouldBe(applied ? 1 : 0);
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_input.lease.renewals",
            1,
            new Dictionary<string, object?> { ["result"] = expectedResult });
        probe.MeasurementsFor("fluxflow.durable_input.messages").Count.ShouldBe(applied ? 1 : 0);
        AssertDoubleMeasurement(
            probe,
            "fluxflow.durable_input.processing.duration",
            1_000,
            new Dictionary<string, object?>());
        AssertInputActivity(probe, envelope, DurableInputAcknowledgementMode.WorkflowCompleted);
    }

    [Fact]
    public async Task Store_failure_records_fixed_operation_without_extra_calls_and_preserves_inner_exception()
    {
        var failure = new IOException("private-store-detail");
        var store = new DurableInputTestStore { LeaseException = failure };
        await using var host = await DurableInputTestApplication.CreateAsync(start: false);
        using var probe = new TelemetryProbe();

        var exception = await Should.ThrowAsync<Exception>(() => Dispatcher(
                store,
                host.Application,
                new SteppingTimeProvider(DurableInputTestData.Now))
            .ProcessOnceAsync().AsTask());

        exception.Message.ShouldBe("Durable input store operation 'lease' failed.");
        exception.InnerException.ShouldBeSameAs(failure);
        store.LeaseCalls.ShouldBe(1);
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_input.store.failures",
            1,
            new Dictionary<string, object?> { ["operation"] = "lease" });
        probe.MeasurementsFor("fluxflow.durable_input.leases.acquired").ShouldBeEmpty();
        probe.MeasurementsFor("fluxflow.durable_input.processing.duration").ShouldBeEmpty();
        probe.StartedActivities.ShouldBeEmpty();
    }

    [Fact]
    public async Task Caller_cancellation_preserves_token_and_finalizes_duration_and_activity_once()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new DurableInputTestStore();
        var envelope = DurableInputTestData.Envelope();
        await store.EnqueueAsync(envelope);
        await using var host = await DurableInputTestApplication.CreateAsync();
        using var probe = new TelemetryProbe();

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => Dispatcher(
                store,
                host.Application,
                new SteppingTimeProvider(DurableInputTestData.Now),
                contract: new CancelingContract(cancellation))
            .ProcessOnceAsync(cancellation.Token).AsTask());

        exception.CancellationToken.ShouldBe(cancellation.Token);
        store.LeaseCalls.ShouldBe(1);
        store.DeliveredTransitions.ShouldBeEmpty();
        store.Releases.ShouldBeEmpty();
        store.DeadLetters.ShouldBeEmpty();
        probe.MeasurementsFor("fluxflow.durable_input.processing.duration").Count.ShouldBe(1);
        probe.MeasurementsFor("fluxflow.durable_input.messages").ShouldBeEmpty();
        var activity = probe.StoppedActivities.ShouldHaveSingleItem();
        activity.Status.ShouldBe(ActivityStatusCode.Error);
        activity.StatusDescription.ShouldBe("canceled");
        activity.TagObjects.ToDictionary().ShouldBe(new Dictionary<string, object?>
        {
            ["flow.trace_id"] = envelope.TraceId.Value,
            ["attempt"] = 1,
            ["acknowledgement.mode"] = nameof(DurableInputAcknowledgementMode.EngineAccepted),
            ["outcome"] = "canceled"
        });
    }

    [Fact]
    public async Task Throwing_metric_and_activity_listeners_do_not_change_delivered_behavior()
    {
        var store = new DurableInputTestStore();
        var envelope = DurableInputTestData.Envelope();
        await store.EnqueueAsync(envelope);
        await using var host = await DurableInputTestApplication.CreateAsync();
        using var probe = new TelemetryProbe(throwMetrics: true, throwOnActivityStart: true);

        var processed = await Dispatcher(
                store,
                host.Application,
                new SteppingTimeProvider(DurableInputTestData.Now),
                contract: new FixedSendContract(PortSendStatus.Accepted))
            .ProcessOnceAsync();

        processed.ShouldBeTrue();
        store.LeaseCalls.ShouldBe(1);
        store.DeliveredTransitions.ShouldHaveSingleItem();
        store.Get(envelope.Key).State.ShouldBe(DurableInputState.Delivered);
        Activity.Current.ShouldBeNull();
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
                contract ?? new FixedSendContract(PortSendStatus.Accepted)
            ]),
            application,
            options ?? DurableInputOptions.Default,
            clock,
            NullLogger<DurableInputDispatcher>.Instance);

    private static DurableInputOptions WorkflowOptions()
        => new(
            batchSize: 1,
            leaseDuration: TimeSpan.FromSeconds(30),
            pollInterval: TimeSpan.FromMilliseconds(250),
            retryDelay: TimeSpan.FromSeconds(1),
            storeFailureDelay: TimeSpan.FromSeconds(2),
            maxDeliveryAttempts: 3,
            DurableInputAcknowledgementMode.WorkflowCompleted,
            workflowCompletionTimeout: TimeSpan.FromSeconds(20),
            leaseRenewalInterval: TimeSpan.FromSeconds(1));

    private static void AssertInstrument<TInstrument>(
        TelemetryProbe probe,
        string name,
        string unit)
        where TInstrument : Instrument
    {
        var instrument = probe.Instruments[name];
        instrument.ShouldBeOfType<TInstrument>();
        instrument.Meter.Name.ShouldBe("FluxFlow.Engine.DurableInput");
        instrument.Unit.ShouldBe(unit);
    }

    private static void AssertLongMeasurement(
        TelemetryProbe probe,
        string name,
        long value,
        IReadOnlyDictionary<string, object?> tags)
    {
        var measurement = probe.MeasurementsFor(name).ShouldHaveSingleItem();
        measurement.Value.ShouldBe(value);
        measurement.Tags.ShouldBe(tags);
    }

    private static void AssertDoubleMeasurement(
        TelemetryProbe probe,
        string name,
        double value,
        IReadOnlyDictionary<string, object?> tags)
    {
        var measurement = probe.MeasurementsFor(name).ShouldHaveSingleItem();
        measurement.Value.ShouldBe(value);
        measurement.Tags.ShouldBe(tags);
    }

    private static void AssertInputActivity(
        TelemetryProbe probe,
        DurableInputEnvelope envelope,
        DurableInputAcknowledgementMode acknowledgementMode)
    {
        var started = probe.StartedActivities.ShouldHaveSingleItem();
        var stopped = probe.StoppedActivities.ShouldHaveSingleItem();
        stopped.ShouldBeSameAs(started);
        stopped.Source.Name.ShouldBe("FluxFlow.Engine.DurableInput");
        stopped.OperationName.ShouldBe("fluxflow.durable_input.process");
        stopped.Kind.ShouldBe(ActivityKind.Consumer);
        stopped.Status.ShouldBe(ActivityStatusCode.Unset);
        stopped.TagObjects.ToDictionary().ShouldBe(new Dictionary<string, object?>
        {
            ["flow.trace_id"] = envelope.TraceId.Value,
            ["attempt"] = 1,
            ["acknowledgement.mode"] = acknowledgementMode.ToString()
        });
    }

    private static void AssertMetricPrivacy(
        TelemetryProbe probe,
        DurableInputEnvelope envelope)
    {
        var allowedTagNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "outcome",
            "result",
            "operation",
            "failure.kind",
            "acknowledgement.mode"
        };
        foreach (var measurement in probe.Measurements)
            measurement.Tags.Keys.ShouldAllBe(tagName => allowedTagNames.Contains(tagName));

        var values = string.Join('|', probe.Measurements.SelectMany(static item => item.Tags.Values));
        values.ShouldNotContain(envelope.Address.Value);
        values.ShouldNotContain(envelope.MessageId.Value);
        values.ShouldNotContain(envelope.TraceId.Value);
        values.ShouldNotContain(envelope.Payload.GetRawText());
        values.ShouldNotContain(envelope.Headers["source"]);
    }

    private sealed class FixedSendContract(PortSendStatus status) : IDurableInputContract
    {
        public string Name => "text-v1";
        public Type PayloadType => typeof(string);
        public bool IsEquivalentTo(IDurableInputContract other) => ReferenceEquals(this, other);
        public DurableInputEnvelope CreateEnvelope<TMessage>(
            FluxFlow.Composition.Addressing.ApplicationAddress address,
            FlowMessage<TMessage> message,
            DateTimeOffset enqueuedAt) => throw new NotSupportedException();
        public ValueTask<PortSendResult> RestoreAndSendAsync(
            FluxFlowApplication application,
            DurableInputEnvelope envelope,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new PortSendResult { Port = envelope.Address, Status = status });
    }

    private sealed class CancelingContract(CancellationTokenSource cancellation) : IDurableInputContract
    {
        public string Name => "text-v1";
        public Type PayloadType => typeof(string);
        public bool IsEquivalentTo(IDurableInputContract other) => ReferenceEquals(this, other);
        public DurableInputEnvelope CreateEnvelope<TMessage>(
            FluxFlow.Composition.Addressing.ApplicationAddress address,
            FlowMessage<TMessage> message,
            DateTimeOffset enqueuedAt) => throw new NotSupportedException();
        public ValueTask<PortSendResult> RestoreAndSendAsync(
            FluxFlowApplication application,
            DurableInputEnvelope envelope,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<PortSendResult>(cancellationToken);
        }
    }

    private sealed class ControlledCompletionSource :
        IDurableInputCompletionSource,
        IDurableInputCompletionSubscription
    {
        private readonly TaskCompletionSource<DurableInputCompletionResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Subscribed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DurableInputCompletionResult> Completion => _completion.Task;

        public ValueTask<IDurableInputCompletionSubscription> SubscribeAsync(
            DurableInputLease lease,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Subscribed.TrySetResult();
            return ValueTask.FromResult<IDurableInputCompletionSubscription>(this);
        }

        public void Complete() => _completion.TrySetResult(DurableInputCompletionResult.Completed);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private class SteppingTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override long GetTimestamp()
            => Interlocked.Add(ref _timestamp, TimeSpan.FromMilliseconds(10).Ticks);
    }

    private sealed class GatedFakeTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
    {
        public TaskCompletionSource TimerCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            TimerCreated.TrySetResult();
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class TelemetryProbe : IDisposable
    {
        private static readonly AsyncLocal<Guid?> ActiveScope = new();
        private readonly Guid _scope = Guid.NewGuid();
        private readonly Guid? _previousScope;
        private readonly bool _throwMetrics;
        private readonly bool _throwOnActivityStart;
        private readonly MeterListener _meterListener = new();
        private readonly ActivityListener _activityListener;

        public TelemetryProbe(bool throwMetrics = false, bool throwOnActivityStart = false)
        {
            _previousScope = ActiveScope.Value;
            ActiveScope.Value = _scope;
            _throwMetrics = throwMetrics;
            _throwOnActivityStart = throwOnActivityStart;
            _meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name != "FluxFlow.Engine.DurableInput")
                    return;
                Instruments.TryAdd(instrument.Name, instrument);
                listener.EnableMeasurementEvents(instrument);
            };
            _meterListener.SetMeasurementEventCallback<long>(Record);
            _meterListener.SetMeasurementEventCallback<double>(Record);
            _meterListener.Start();

            _activityListener = new ActivityListener
            {
                ShouldListenTo = static source => source.Name == "FluxFlow.Engine.DurableInput",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActiveScope.Value == _scope
                        ? ActivitySamplingResult.AllDataAndRecorded
                        : ActivitySamplingResult.None,
                ActivityStarted = activity =>
                {
                    StartedActivities.Enqueue(activity);
                    if (_throwOnActivityStart)
                        throw new InvalidOperationException("host activity listener failed");
                },
                ActivityStopped = activity => StoppedActivities.Enqueue(activity)
            };
            ActivitySource.AddActivityListener(_activityListener);
        }

        public ConcurrentDictionary<string, Instrument> Instruments { get; } = new();
        public ConcurrentQueue<Measurement> Measurements { get; } = new();
        public ConcurrentQueue<Activity> StartedActivities { get; } = new();
        public ConcurrentQueue<Activity> StoppedActivities { get; } = new();

        public IReadOnlyList<Measurement> MeasurementsFor(string name)
            => Measurements.Where(item => item.InstrumentName == name).ToArray();

        public void Dispose()
        {
            _activityListener.Dispose();
            _meterListener.Dispose();
            ActiveScope.Value = _previousScope;
        }

        private void Record<T>(
            Instrument instrument,
            T value,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
            where T : struct
        {
            if (ActiveScope.Value != _scope)
                return;
            if (_throwMetrics)
                throw new InvalidOperationException("host metric listener failed");
            Measurements.Enqueue(new Measurement(
                instrument.Name,
                value,
                tags.ToArray().ToDictionary(static item => item.Key, static item => item.Value)));
        }
    }

    private sealed record Measurement(
        string InstrumentName,
        object Value,
        IReadOnlyDictionary<string, object?> Tags);

    public enum InputOutcome
    {
        Delivered,
        Retry,
        DeadLetter
    }
}
