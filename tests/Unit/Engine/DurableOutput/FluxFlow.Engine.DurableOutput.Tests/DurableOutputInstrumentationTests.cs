using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputInstrumentationTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Instruments_have_exact_names_types_units_and_meter()
    {
        using var probe = new TelemetryProbe();

        DurableOutputInstrumentation.RecordLeaseAcquired();

        probe.Instruments.Keys.Order(StringComparer.Ordinal).ShouldBe(
        [
            "fluxflow.durable_output.capture.duration",
            "fluxflow.durable_output.captures",
            "fluxflow.durable_output.deliveries",
            "fluxflow.durable_output.delivery.duration",
            "fluxflow.durable_output.handler.calls",
            "fluxflow.durable_output.lease.renewals",
            "fluxflow.durable_output.leases.acquired",
            "fluxflow.durable_output.store.failures"
        ]);
        AssertInstrument<Counter<long>>(probe, "fluxflow.durable_output.captures", "{capture}");
        AssertInstrument<Histogram<double>>(probe, "fluxflow.durable_output.capture.duration", "ms");
        AssertInstrument<Counter<long>>(probe, "fluxflow.durable_output.leases.acquired", "{lease}");
        AssertInstrument<Counter<long>>(probe, "fluxflow.durable_output.handler.calls", "{call}");
        AssertInstrument<Counter<long>>(probe, "fluxflow.durable_output.deliveries", "{message}");
        AssertInstrument<Counter<long>>(probe, "fluxflow.durable_output.lease.renewals", "{renewal}");
        AssertInstrument<Counter<long>>(probe, "fluxflow.durable_output.store.failures", "{failure}");
        AssertInstrument<Histogram<double>>(probe, "fluxflow.durable_output.delivery.duration", "ms");
    }

    [Theory]
    [InlineData(CaptureCase.Enqueued, "enqueued", 1)]
    [InlineData(CaptureCase.AlreadyExists, "already_exists", 1)]
    [InlineData(CaptureCase.Conflict, "conflict", 1)]
    [InlineData(CaptureCase.Canceled, "canceled", 0)]
    [InlineData(CaptureCase.Failed, "failed", 1)]
    public async Task Capture_records_exact_result_duration_activity_and_preserves_behavior(
        CaptureCase scenario,
        string expectedResult,
        int expectedStoreCalls)
    {
        var expectedFailure = new IOException("private-store-failure");
        var store = new RecordingDurableOutputStore
        {
            Status = scenario switch
            {
                CaptureCase.AlreadyExists => DurableOutputEnqueueStatus.AlreadyExists,
                CaptureCase.Conflict => DurableOutputEnqueueStatus.Conflict,
                _ => DurableOutputEnqueueStatus.Enqueued
            },
            EnqueueException = scenario == CaptureCase.Failed ? expectedFailure : null
        };
        var clock = new SteppingTimeProvider(DurableOutputTestData.CapturedAt);
        var message = FlowMessage.Restore(
            "private-payload",
            new MessageId("private-message"),
            new TraceId("private-trace"),
            DurableOutputTestData.MessageTimestamp,
            new CorrelationId("private-correlation"),
            new MessageId("private-cause"),
            new Dictionary<string, string> { ["private-header"] = "private-value" });
        using var cancellation = new CancellationTokenSource();
        if (scenario == CaptureCase.Canceled)
            cancellation.Cancel();
        using var probe = new TelemetryProbe();
        using var parent = new Activity("capture-parent").SetIdFormat(ActivityIdFormat.W3C).Start();
        Exception? observed = null;

        try
        {
            await Capture(store, clock).CaptureAsync(message, cancellation.Token);
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        switch (scenario)
        {
            case CaptureCase.Enqueued:
            case CaptureCase.AlreadyExists:
                observed.ShouldBeNull();
                break;
            case CaptureCase.Conflict:
                observed.ShouldBeOfType<InvalidOperationException>().Message
                    .ShouldContain("conflicts with different persisted content");
                break;
            case CaptureCase.Canceled:
                observed.ShouldBeOfType<OperationCanceledException>().CancellationToken
                    .ShouldBe(cancellation.Token);
                break;
            case CaptureCase.Failed:
                observed.ShouldBeSameAs(expectedFailure);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        store.Envelopes.Count.ShouldBe(expectedStoreCalls);
        store.CancellationTokens.Count.ShouldBe(expectedStoreCalls);
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_output.captures",
            new Dictionary<string, object?> { ["result"] = expectedResult });
        AssertDoubleMeasurement(
            probe,
            "fluxflow.durable_output.capture.duration",
            10,
            new Dictionary<string, object?> { ["result"] = expectedResult });
        var activity = probe.StoppedActivities.ShouldHaveSingleItem();
        activity.Source.Name.ShouldBe("FluxFlow.Engine.DurableOutput");
        activity.OperationName.ShouldBe("fluxflow.durable_output.capture");
        activity.Kind.ShouldBe(ActivityKind.Producer);
        activity.ParentSpanId.ShouldBe(parent.SpanId);
        activity.Status.ShouldBe(expectedResult is "enqueued" or "already_exists"
            ? ActivityStatusCode.Unset
            : ActivityStatusCode.Error);
        activity.TagObjects.ToDictionary().ShouldBe(new Dictionary<string, object?>
        {
            ["flow.trace_id"] = message.TraceId.Value,
            ["outcome"] = expectedResult
        });
        AssertMetricPrivacy(probe, message.TraceId.Value, message.MessageId.Value, "private-payload");
    }

    [Theory]
    [InlineData(DeliveryCase.CompletedApplied, "succeeded", "completed", "applied")]
    [InlineData(DeliveryCase.CompletedRejected, "succeeded", "completed", "rejected")]
    [InlineData(DeliveryCase.RetryApplied, "failed", "retry", "applied")]
    [InlineData(DeliveryCase.RetryRejected, "failed", "retry", "rejected")]
    [InlineData(DeliveryCase.DeadLetterApplied, "failed", "dead_letter", "applied")]
    [InlineData(DeliveryCase.DeadLetterRejected, "failed", "dead_letter", "rejected")]
    public async Task Delivery_records_exact_handler_transition_activity_and_store_calls(
        DeliveryCase scenario,
        string expectedHandlerResult,
        string expectedOutcome,
        string expectedTransitionResult)
    {
        var clock = new SteppingTimeProvider(DurableOutputTestData.CapturedAt);
        var envelope = DurableOutputTestData.Envelope();
        var store = DeliveryStore.ForLease(envelope, scenario.IsApplied());
        var handler = new DeliveryHandler
        {
            OnDeliver = scenario.IsCompleted()
                ? static (_, _) => ValueTask.CompletedTask
                : static (_, _) => throw new InvalidOperationException("private-handler-failure")
        };
        var options = Options(scenario.IsDeadLetter() ? 1 : null);
        using var probe = new TelemetryProbe();

        var processed = await Dispatcher(store, handler, clock, options).ProcessOnceAsync();

        processed.ShouldBeTrue();
        store.LeaseCalls.ShouldBe(1);
        handler.Calls.ShouldBe(1);
        store.RenewalCalls.ShouldBe(0);
        store.CompletionCalls.ShouldBe(scenario.IsCompleted() ? 1 : 0);
        store.RetryCalls.ShouldBe(scenario.IsRetry() ? 1 : 0);
        store.DeadLetterCalls.ShouldBe(scenario.IsDeadLetter() ? 1 : 0);
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_output.leases.acquired",
            new Dictionary<string, object?>());
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_output.handler.calls",
            new Dictionary<string, object?> { ["result"] = expectedHandlerResult });
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_output.deliveries",
            new Dictionary<string, object?>
            {
                ["outcome"] = expectedOutcome,
                ["result"] = expectedTransitionResult
            });
        AssertDoubleMeasurement(
            probe,
            "fluxflow.durable_output.delivery.duration",
            10,
            new Dictionary<string, object?>());
        AssertDeliveryActivity(probe, envelope, expectedOutcome);
        AssertMetricPrivacy(probe, envelope.TraceId.Value, envelope.MessageId.Value, envelope.Payload.GetRawText());
    }

    [Theory]
    [InlineData(true, "applied", "succeeded", "completed")]
    [InlineData(false, "rejected", "canceled", "ownership_lost")]
    public async Task Renewal_records_exact_result_and_handler_ownership_outcome(
        bool applied,
        string expectedRenewalResult,
        string expectedHandlerResult,
        string expectedOutcome)
    {
        var clock = new GatedFakeTimeProvider(DurableOutputTestData.CapturedAt);
        var envelope = DurableOutputTestData.Envelope();
        var store = DeliveryStore.ForLease(envelope, applied);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DeliveryHandler
        {
            OnDeliver = async (_, token) =>
            {
                entered.TrySetResult();
                try
                {
                    await release.Task.WaitAsync(token);
                }
                finally
                {
                    if (token.IsCancellationRequested)
                        canceled.TrySetResult();
                }
            }
        };
        using var probe = new TelemetryProbe();
        var execution = Dispatcher(store, handler, clock, Options()).ProcessOnceAsync().AsTask();

        await entered.Task.WaitAsync(WaitTimeout);
        await clock.TimerCreated.Task.WaitAsync(WaitTimeout);
        clock.Advance(Options().LeaseRenewalInterval);
        await store.RenewalObserved.Task.WaitAsync(WaitTimeout);
        if (applied)
            release.TrySetResult();
        else
            await canceled.Task.WaitAsync(WaitTimeout);
        (await execution.WaitAsync(WaitTimeout)).ShouldBeTrue();

        store.LeaseCalls.ShouldBe(1);
        store.RenewalCalls.ShouldBe(1);
        store.CompletionCalls.ShouldBe(applied ? 1 : 0);
        handler.Calls.ShouldBe(1);
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_output.lease.renewals",
            new Dictionary<string, object?> { ["result"] = expectedRenewalResult });
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_output.handler.calls",
            new Dictionary<string, object?> { ["result"] = expectedHandlerResult });
        var deliveryTags = applied
            ? new Dictionary<string, object?> { ["outcome"] = "completed", ["result"] = "applied" }
            : new Dictionary<string, object?> { ["outcome"] = "ownership_lost" };
        AssertLongMeasurement(probe, "fluxflow.durable_output.deliveries", deliveryTags);
        AssertDoubleMeasurement(
            probe,
            "fluxflow.durable_output.delivery.duration",
            1_000,
            new Dictionary<string, object?>());
        AssertDeliveryActivity(probe, envelope, expectedOutcome);
    }

    [Fact]
    public async Task Caller_cancellation_records_handler_canceled_and_finalizes_without_settlement()
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new GatedFakeTimeProvider(DurableOutputTestData.CapturedAt);
        var envelope = DurableOutputTestData.Envelope();
        var store = DeliveryStore.ForLease(envelope, renewalApplied: true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompletes = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DeliveryHandler
        {
            OnDeliver = async (_, token) =>
            {
                entered.TrySetResult();
                try
                {
                    await neverCompletes.Task.WaitAsync(token);
                }
                finally
                {
                    observed.TrySetResult();
                }
            }
        };
        using var probe = new TelemetryProbe();
        var execution = Dispatcher(store, handler, clock, Options())
            .ProcessOnceAsync(cancellation.Token).AsTask();

        await entered.Task.WaitAsync(WaitTimeout);
        cancellation.Cancel();
        var exception = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await execution.WaitAsync(WaitTimeout));
        await observed.Task.WaitAsync(WaitTimeout);

        cancellation.IsCancellationRequested.ShouldBeTrue();
        exception.CancellationToken.IsCancellationRequested.ShouldBeTrue();
        store.CompletionCalls.ShouldBe(0);
        store.RetryCalls.ShouldBe(0);
        store.DeadLetterCalls.ShouldBe(0);
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_output.handler.calls",
            new Dictionary<string, object?> { ["result"] = "canceled" });
        probe.MeasurementsFor("fluxflow.durable_output.deliveries").ShouldBeEmpty();
        var activity = probe.StoppedActivities.ShouldHaveSingleItem();
        activity.Status.ShouldBe(ActivityStatusCode.Error);
        activity.TagObjects.ToDictionary()["outcome"].ShouldBe("canceled");
        probe.MeasurementsFor("fluxflow.durable_output.delivery.duration").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Store_failure_records_fixed_operation_without_extra_calls_and_preserves_inner_exception()
    {
        var expected = new IOException("private-store-detail");
        var store = new DeliveryStore { LeaseException = expected };
        using var probe = new TelemetryProbe();

        var exception = await Should.ThrowAsync<DurableOutputDeliveryDispatcher.DurableOutputDeliveryStoreException>(
            () => Dispatcher(
                    store,
                    new DeliveryHandler(),
                    new SteppingTimeProvider(DurableOutputTestData.CapturedAt),
                    Options())
                .ProcessOnceAsync().AsTask());

        exception.Operation.ShouldBe("lease");
        exception.InnerException.ShouldBeSameAs(expected);
        store.LeaseCalls.ShouldBe(1);
        store.TotalTransitionCalls.ShouldBe(0);
        AssertLongMeasurement(
            probe,
            "fluxflow.durable_output.store.failures",
            new Dictionary<string, object?> { ["operation"] = "lease" });
        probe.MeasurementsFor("fluxflow.durable_output.leases.acquired").ShouldBeEmpty();
        probe.MeasurementsFor("fluxflow.durable_output.delivery.duration").ShouldBeEmpty();
        probe.StartedActivities.ShouldBeEmpty();
    }

    [Fact]
    public async Task Throwing_capture_and_delivery_listeners_do_not_change_behavior_or_store_calls()
    {
        var captureStore = new RecordingDurableOutputStore();
        var envelope = DurableOutputTestData.Envelope();
        var deliveryStore = DeliveryStore.ForLease(envelope, renewalApplied: true);
        var handler = new DeliveryHandler();
        using var probe = new TelemetryProbe(throwMetrics: true, throwOnActivityStart: true);

        await Capture(captureStore, new SteppingTimeProvider(DurableOutputTestData.CapturedAt))
            .CaptureAsync(FlowMessage.Create("captured"));
        var processed = await Dispatcher(
                deliveryStore,
                handler,
                new SteppingTimeProvider(DurableOutputTestData.CapturedAt),
                Options())
            .ProcessOnceAsync();

        captureStore.Envelopes.ShouldHaveSingleItem();
        processed.ShouldBeTrue();
        deliveryStore.LeaseCalls.ShouldBe(1);
        deliveryStore.CompletionCalls.ShouldBe(1);
        handler.Calls.ShouldBe(1);
        Activity.Current.ShouldBeNull();
    }

    private static DurableOutputCapture<string> Capture(
        IDurableOutputStore store,
        TimeProvider clock)
        => new(
            DurableOutputTestData.Output,
            "text-v1",
            DurableOutputTestData.TypeInfo<string>(),
            store,
            clock);

    private static DurableOutputDeliveryDispatcher Dispatcher(
        IDurableOutputDeliveryStore store,
        IDurableOutputDeliveryHandler handler,
        TimeProvider clock,
        DurableOutputDeliveryOptions options)
        => new(store, handler, options, clock, NullLogger<DurableOutputDeliveryDispatcher>.Instance);

    private static DurableOutputDeliveryOptions Options(int? maximumAttempts = null)
        => new(
            leaseDuration: TimeSpan.FromSeconds(30),
            leaseRenewalInterval: TimeSpan.FromSeconds(1),
            retryDelay: TimeSpan.FromSeconds(2),
            idleDelay: TimeSpan.FromMilliseconds(250),
            maximumAttempts);

    private static void AssertInstrument<TInstrument>(
        TelemetryProbe probe,
        string name,
        string unit)
        where TInstrument : Instrument
    {
        var instrument = probe.Instruments[name];
        instrument.ShouldBeOfType<TInstrument>();
        instrument.Meter.Name.ShouldBe("FluxFlow.Engine.DurableOutput");
        instrument.Unit.ShouldBe(unit);
    }

    private static void AssertLongMeasurement(
        TelemetryProbe probe,
        string name,
        IReadOnlyDictionary<string, object?> tags)
    {
        var measurement = probe.MeasurementsFor(name).ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1L);
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

    private static void AssertDeliveryActivity(
        TelemetryProbe probe,
        DurableOutputEnvelope envelope,
        string outcome)
    {
        var started = probe.StartedActivities.ShouldHaveSingleItem();
        var stopped = probe.StoppedActivities.ShouldHaveSingleItem();
        stopped.ShouldBeSameAs(started);
        stopped.Source.Name.ShouldBe("FluxFlow.Engine.DurableOutput");
        stopped.OperationName.ShouldBe("fluxflow.durable_output.deliver");
        stopped.Kind.ShouldBe(ActivityKind.Consumer);
        stopped.Status.ShouldBe(outcome == "completed" ? ActivityStatusCode.Unset : ActivityStatusCode.Error);
        stopped.TagObjects.ToDictionary().ShouldBe(new Dictionary<string, object?>
        {
            ["flow.trace_id"] = envelope.TraceId.Value,
            ["attempt"] = 1,
            ["outcome"] = outcome
        });
    }

    private static void AssertMetricPrivacy(
        TelemetryProbe probe,
        string traceId,
        string messageId,
        string payload)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "outcome",
            "result",
            "operation",
            "failure.kind",
            "acknowledgement.mode"
        };
        foreach (var measurement in probe.Measurements)
            measurement.Tags.Keys.ShouldAllBe(tagName => allowed.Contains(tagName));
        var values = string.Join('|', probe.Measurements.SelectMany(static item => item.Tags.Values));
        values.ShouldNotContain(traceId);
        values.ShouldNotContain(messageId);
        values.ShouldNotContain(payload);
        values.ShouldNotContain("private-handler-failure");
        values.ShouldNotContain("private-store-failure");
    }

    private sealed class DeliveryStore : IDurableOutputDeliveryStore
    {
        private DurableOutputEnvelope? _envelope;
        private bool _renewalApplied = true;

        public Exception? LeaseException { get; init; }
        public int LeaseCalls { get; private set; }
        public int RenewalCalls { get; private set; }
        public int CompletionCalls { get; private set; }
        public int RetryCalls { get; private set; }
        public int DeadLetterCalls { get; private set; }
        public int TotalTransitionCalls => RenewalCalls + CompletionCalls + RetryCalls + DeadLetterCalls;
        public bool TransitionApplied { get; private set; } = true;
        public TaskCompletionSource RenewalObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static DeliveryStore ForLease(DurableOutputEnvelope envelope, bool renewalApplied)
            => new() { _envelope = envelope, _renewalApplied = renewalApplied, TransitionApplied = renewalApplied };

        public ValueTask<DurableOutputDeliveryLease?> TryLeaseAsync(
            DurableOutputDeliveryLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LeaseCalls++;
            if (LeaseException is not null)
                throw LeaseException;
            if (_envelope is null)
                return ValueTask.FromResult<DurableOutputDeliveryLease?>(null);
            return ValueTask.FromResult<DurableOutputDeliveryLease?>(new(
                _envelope,
                Guid.NewGuid(),
                request.OwnerId,
                request.Now,
                request.LeaseUntil,
                attempt: 1));
        }

        public ValueTask<DurableOutputDeliveryTransitionResult> RenewLeaseAsync(
            DurableOutputDeliveryLeaseRenewal renewal,
            CancellationToken cancellationToken = default)
        {
            RenewalCalls++;
            RenewalObserved.TrySetResult();
            return ValueTask.FromResult(Result(
                renewal.Key,
                _renewalApplied ? DurableOutputDeliveryTransitionStatus.Applied : DurableOutputDeliveryTransitionStatus.LeaseLost));
        }

        public ValueTask<DurableOutputDeliveryTransitionResult> CompleteAsync(
            DurableOutputDeliveryTransition transition,
            CancellationToken cancellationToken = default)
        {
            CompletionCalls++;
            return ValueTask.FromResult(Result(transition.Key, Status()));
        }

        public ValueTask<DurableOutputDeliveryTransitionResult> RetryAsync(
            DurableOutputDeliveryRetry retry,
            CancellationToken cancellationToken = default)
        {
            RetryCalls++;
            return ValueTask.FromResult(Result(retry.Key, Status()));
        }

        public ValueTask<DurableOutputDeliveryTransitionResult> DeadLetterAsync(
            DurableOutputDeliveryDeadLetter deadLetter,
            CancellationToken cancellationToken = default)
        {
            DeadLetterCalls++;
            return ValueTask.FromResult(Result(deadLetter.Key, Status()));
        }

        private DurableOutputDeliveryTransitionStatus Status()
            => TransitionApplied
                ? DurableOutputDeliveryTransitionStatus.Applied
                : DurableOutputDeliveryTransitionStatus.LeaseLost;

        private static DurableOutputDeliveryTransitionResult Result(
            DurableOutputKey key,
            DurableOutputDeliveryTransitionStatus status)
            => new(key, status);
    }

    private sealed class DeliveryHandler : IDurableOutputDeliveryHandler
    {
        public int Calls { get; private set; }
        public Func<DurableOutputEnvelope, CancellationToken, ValueTask> OnDeliver { get; init; } =
            static (_, _) => ValueTask.CompletedTask;

        public ValueTask DeliverAsync(
            DurableOutputEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Calls++;
            return OnDeliver(envelope, cancellationToken);
        }
    }

    private sealed class SteppingTimeProvider(DateTimeOffset utcNow) : TimeProvider
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
                if (instrument.Meter.Name != "FluxFlow.Engine.DurableOutput")
                    return;
                Instruments.TryAdd(instrument.Name, instrument);
                listener.EnableMeasurementEvents(instrument);
            };
            _meterListener.SetMeasurementEventCallback<long>(Record);
            _meterListener.SetMeasurementEventCallback<double>(Record);
            _meterListener.Start();
            _activityListener = new ActivityListener
            {
                ShouldListenTo = static source => source.Name == "FluxFlow.Engine.DurableOutput",
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

    public enum CaptureCase
    {
        Enqueued,
        AlreadyExists,
        Conflict,
        Canceled,
        Failed
    }

    public enum DeliveryCase
    {
        CompletedApplied,
        CompletedRejected,
        RetryApplied,
        RetryRejected,
        DeadLetterApplied,
        DeadLetterRejected
    }
}

file static class DeliveryCaseExtensions
{
    public static bool IsCompleted(this DurableOutputInstrumentationTests.DeliveryCase value)
        => value is DurableOutputInstrumentationTests.DeliveryCase.CompletedApplied or
            DurableOutputInstrumentationTests.DeliveryCase.CompletedRejected;

    public static bool IsRetry(this DurableOutputInstrumentationTests.DeliveryCase value)
        => value is DurableOutputInstrumentationTests.DeliveryCase.RetryApplied or
            DurableOutputInstrumentationTests.DeliveryCase.RetryRejected;

    public static bool IsDeadLetter(this DurableOutputInstrumentationTests.DeliveryCase value)
        => value is DurableOutputInstrumentationTests.DeliveryCase.DeadLetterApplied or
            DurableOutputInstrumentationTests.DeliveryCase.DeadLetterRejected;

    public static bool IsApplied(this DurableOutputInstrumentationTests.DeliveryCase value)
        => value is DurableOutputInstrumentationTests.DeliveryCase.CompletedApplied or
            DurableOutputInstrumentationTests.DeliveryCase.RetryApplied or
            DurableOutputInstrumentationTests.DeliveryCase.DeadLetterApplied;
}
