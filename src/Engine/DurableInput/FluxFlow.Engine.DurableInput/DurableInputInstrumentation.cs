using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FluxFlow.Engine.DurableInput;

internal static class DurableInputInstrumentation
{
    internal const string ActivitySourceName = "FluxFlow.Engine.DurableInput";
    internal const string MeterName = "FluxFlow.Engine.DurableInput";
    internal const string ProcessActivityName = "fluxflow.durable_input.process";
    internal const string LeasesAcquiredName = "fluxflow.durable_input.leases.acquired";
    internal const string MessagesName = "fluxflow.durable_input.messages";
    internal const string LeaseRenewalsName = "fluxflow.durable_input.lease.renewals";
    internal const string StoreFailuresName = "fluxflow.durable_input.store.failures";
    internal const string ProcessingDurationName = "fluxflow.durable_input.processing.duration";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long>? LeasesAcquired =
        CreateInstrument(() => Meter.CreateCounter<long>(LeasesAcquiredName, "{lease}"));
    private static readonly Counter<long>? Messages =
        CreateInstrument(() => Meter.CreateCounter<long>(MessagesName, "{message}"));
    private static readonly Counter<long>? LeaseRenewals =
        CreateInstrument(() => Meter.CreateCounter<long>(LeaseRenewalsName, "{renewal}"));
    private static readonly Counter<long>? StoreFailures =
        CreateInstrument(() => Meter.CreateCounter<long>(StoreFailuresName, "{failure}"));
    private static readonly Histogram<double>? ProcessingDuration =
        CreateInstrument(() => Meter.CreateHistogram<double>(ProcessingDurationName, "ms"));

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

    internal static void RecordMessage(
        string outcome,
        DurableInputFailureKind? failureKind = null)
    {
        try
        {
            if (Messages?.Enabled != true)
                return;

            var tags = new TagList { { "outcome", outcome } };
            if (failureKind is not null)
                tags.Add("failure.kind", failureKind.Value.ToString());
            Messages.Add(1, tags);
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

    internal static long? StartDuration(TimeProvider clock)
    {
        try
        {
            return ProcessingDuration?.Enabled == true ? clock.GetTimestamp() : null;
        }
        catch
        {
            return null;
        }
    }

    internal static void RecordDuration(TimeProvider clock, long? startedAt)
    {
        if (startedAt is null)
            return;

        try
        {
            ProcessingDuration?.Record(
                clock.GetElapsedTime(startedAt.Value).TotalMilliseconds);
        }
        catch
        {
            // Host-owned metric listeners cannot alter durable processing.
        }
    }

    internal static Activity? StartProcessActivity(
        DurableInputLease lease,
        DurableInputAcknowledgementMode acknowledgementMode)
    {
        var previousActivity = Activity.Current;
        try
        {
            if (!ActivitySource.HasListeners())
                return null;

            var activity = ActivitySource.StartActivity(
                ProcessActivityName,
                ActivityKind.Consumer);
            activity?.SetTag("flow.trace_id", lease.Envelope.TraceId.Value);
            activity?.SetTag("attempt", lease.Attempt);
            activity?.SetTag("acknowledgement.mode", acknowledgementMode.ToString());
            return activity;
        }
        catch
        {
            RestoreAmbientActivity(previousActivity);
            return null;
        }
    }

    internal static void SetActivityFailure(Activity? activity, string outcome)
    {
        try
        {
            activity?.SetTag("outcome", outcome);
            activity?.SetStatus(ActivityStatusCode.Error, outcome);
        }
        catch
        {
            // Host-owned activity listeners cannot alter durable processing.
        }
    }

    internal static void StopActivity(Activity? activity)
    {
        try
        {
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
