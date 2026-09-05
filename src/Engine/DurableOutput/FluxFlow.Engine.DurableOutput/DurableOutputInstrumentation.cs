using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.DurableOutput;

internal static class DurableOutputInstrumentation
{
    internal const string ActivitySourceName = "FluxFlow.Engine.DurableOutput";
    internal const string MeterName = "FluxFlow.Engine.DurableOutput";
    internal const string CaptureActivityName = "fluxflow.durable_output.capture";
    internal const string DeliveryActivityName = "fluxflow.durable_output.deliver";
    internal const string CapturesName = "fluxflow.durable_output.captures";
    internal const string CaptureDurationName = "fluxflow.durable_output.capture.duration";
    internal const string LeasesAcquiredName = "fluxflow.durable_output.leases.acquired";
    internal const string HandlerCallsName = "fluxflow.durable_output.handler.calls";
    internal const string DeliveriesName = "fluxflow.durable_output.deliveries";
    internal const string LeaseRenewalsName = "fluxflow.durable_output.lease.renewals";
    internal const string StoreFailuresName = "fluxflow.durable_output.store.failures";
    internal const string DeliveryDurationName = "fluxflow.durable_output.delivery.duration";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long>? Captures =
        CreateInstrument(() => Meter.CreateCounter<long>(CapturesName, "{capture}"));
    private static readonly Histogram<double>? CaptureDuration =
        CreateInstrument(() => Meter.CreateHistogram<double>(CaptureDurationName, "ms"));
    private static readonly Counter<long>? LeasesAcquired =
        CreateInstrument(() => Meter.CreateCounter<long>(LeasesAcquiredName, "{lease}"));
    private static readonly Counter<long>? HandlerCalls =
        CreateInstrument(() => Meter.CreateCounter<long>(HandlerCallsName, "{call}"));
    private static readonly Counter<long>? Deliveries =
        CreateInstrument(() => Meter.CreateCounter<long>(DeliveriesName, "{message}"));
    private static readonly Counter<long>? LeaseRenewals =
        CreateInstrument(() => Meter.CreateCounter<long>(LeaseRenewalsName, "{renewal}"));
    private static readonly Counter<long>? StoreFailures =
        CreateInstrument(() => Meter.CreateCounter<long>(StoreFailuresName, "{failure}"));
    private static readonly Histogram<double>? DeliveryDuration =
        CreateInstrument(() => Meter.CreateHistogram<double>(DeliveryDurationName, "ms"));

    internal static long? StartCaptureDuration(TimeProvider clock)
        => StartDuration(clock, CaptureDuration);

    internal static Activity? StartCaptureActivity(TraceId traceId)
        => StartActivity(CaptureActivityName, ActivityKind.Producer, traceId, attempt: null);

    internal static void CompleteCapture(
        string result,
        TimeProvider clock,
        long? startedAt,
        Activity? activity)
    {
        try
        {
            Captures?.Add(
                1,
                new KeyValuePair<string, object?>("result", result));
        }
        catch
        {
            // Host-owned metric listeners cannot alter durable processing.
        }

        if (startedAt is not null)
        {
            try
            {
                CaptureDuration?.Record(
                    clock.GetElapsedTime(startedAt.Value).TotalMilliseconds,
                    new KeyValuePair<string, object?>("result", result));
            }
            catch
            {
                // Host-owned metric listeners cannot alter durable processing.
            }
        }

        CompleteActivity(activity, result, result is not ("enqueued" or "already_exists"));
    }

    internal static void RecordLeaseAcquired()
    {
        try
        {
            LeasesAcquired?.Add(1);
        }
        catch
        {
            // Host-owned metric listeners cannot alter durable processing.
        }
    }

    internal static void RecordHandlerCall(string result)
    {
        try
        {
            HandlerCalls?.Add(
                1,
                new KeyValuePair<string, object?>("result", result));
        }
        catch
        {
            // Host-owned metric listeners cannot alter durable processing.
        }
    }

    internal static void RecordDelivery(string outcome, bool? applied = null)
    {
        try
        {
            if (Deliveries?.Enabled != true)
                return;

            var tags = new TagList { { "outcome", outcome } };
            if (applied is not null)
                tags.Add("result", applied.Value ? "applied" : "rejected");
            Deliveries.Add(1, tags);
        }
        catch
        {
            // Host-owned metric listeners cannot alter durable processing.
        }
    }

    internal static void RecordLeaseRenewal(bool applied)
    {
        try
        {
            LeaseRenewals?.Add(
                1,
                new KeyValuePair<string, object?>(
                    "result",
                    applied ? "applied" : "rejected"));
        }
        catch
        {
            // Host-owned metric listeners cannot alter durable processing.
        }
    }

    internal static void RecordStoreFailure(string operation)
    {
        try
        {
            StoreFailures?.Add(
                1,
                new KeyValuePair<string, object?>("operation", operation));
        }
        catch
        {
            // Host-owned metric listeners cannot alter durable processing.
        }
    }

    internal static long? StartDeliveryDuration(TimeProvider clock)
        => StartDuration(clock, DeliveryDuration);

    internal static void RecordDeliveryDuration(TimeProvider clock, long? startedAt)
    {
        if (startedAt is null)
            return;

        try
        {
            DeliveryDuration?.Record(
                clock.GetElapsedTime(startedAt.Value).TotalMilliseconds);
        }
        catch
        {
            // Host-owned metric listeners cannot alter durable processing.
        }
    }

    internal static Activity? StartDeliveryActivity(DurableOutputDeliveryLease lease)
        => StartActivity(
            DeliveryActivityName,
            ActivityKind.Consumer,
            lease.Envelope.TraceId,
            lease.Attempt);

    internal static void CompleteDeliveryActivity(Activity? activity, string outcome)
        => CompleteActivity(activity, outcome, outcome != "completed");

    private static long? StartDuration(
        TimeProvider clock,
        Histogram<double>? histogram)
    {
        try
        {
            return histogram?.Enabled == true ? clock.GetTimestamp() : null;
        }
        catch
        {
            return null;
        }
    }

    private static Activity? StartActivity(
        string name,
        ActivityKind kind,
        TraceId traceId,
        int? attempt)
    {
        var previousActivity = Activity.Current;
        try
        {
            if (!ActivitySource.HasListeners())
                return null;

            var activity = ActivitySource.StartActivity(name, kind);
            activity?.SetTag("flow.trace_id", traceId.Value);
            if (attempt is not null)
                activity?.SetTag("attempt", attempt.Value);
            return activity;
        }
        catch
        {
            RestoreAmbientActivity(previousActivity);
            return null;
        }
    }

    private static void CompleteActivity(
        Activity? activity,
        string outcome,
        bool isError)
    {
        try
        {
            activity?.SetTag("outcome", outcome);
            if (isError)
                activity?.SetStatus(ActivityStatusCode.Error, outcome);
            activity?.Dispose();
        }
        catch
        {
            // Host-owned activity listeners cannot alter durable processing.
        }
    }

    private static TInstrument? CreateInstrument<TInstrument>(Func<TInstrument> create)
        where TInstrument : Instrument
    {
        try
        {
            return create();
        }
        catch
        {
            return null;
        }
    }

    private static void RestoreAmbientActivity(Activity? previousActivity)
    {
        try
        {
            Activity.Current = previousActivity;
        }
        catch
        {
            // Host-owned activity listeners cannot alter durable processing.
        }
    }
}
