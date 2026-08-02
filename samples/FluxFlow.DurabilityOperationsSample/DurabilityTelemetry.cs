using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

internal sealed class DurabilityTelemetry : IDisposable
{
    private const string InputSource = "FluxFlow.Engine.DurableInput";
    private const string OutputSource = "FluxFlow.Engine.DurableOutput";
    private const string InputLease = "fluxflow.durable_input.leases.acquired";
    private const string InputMessage = "fluxflow.durable_input.messages";
    private const string InputDuration = "fluxflow.durable_input.processing.duration";
    private const string OutputCapture = "fluxflow.durable_output.captures";
    private const string OutputCaptureDuration = "fluxflow.durable_output.capture.duration";
    private const string OutputLease = "fluxflow.durable_output.leases.acquired";
    private const string OutputHandler = "fluxflow.durable_output.handler.calls";
    private const string OutputDelivery = "fluxflow.durable_output.deliveries";
    private const string OutputDeliveryDuration = "fluxflow.durable_output.delivery.duration";
    private const string InputActivity = "fluxflow.durable_input.process";
    private const string OutputCaptureActivity = "fluxflow.durable_output.capture";
    private const string OutputDeliveryActivity = "fluxflow.durable_output.deliver";

    private static readonly HashSet<string> KnownInstruments =
    [
        InputLease,
        InputMessage,
        InputDuration,
        OutputCapture,
        OutputCaptureDuration,
        OutputLease,
        OutputHandler,
        OutputDelivery,
        OutputDeliveryDuration
    ];

    private static readonly string[] ExpectedObservationKeys =
    [
        InputLease,
        $"{InputMessage}|outcome=delivered",
        InputDuration,
        $"{OutputCapture}|result=enqueued",
        OutputCaptureDuration,
        OutputLease,
        $"{OutputHandler}|result=succeeded",
        $"{OutputDelivery}|outcome=completed|result=applied",
        OutputDeliveryDuration,
        $"{InputSource}|{InputActivity}|Consumer",
        $"{OutputSource}|{OutputCaptureActivity}|Producer|outcome=enqueued",
        $"{OutputSource}|{OutputDeliveryActivity}|Consumer|outcome=completed"
    ];

    private readonly ConcurrentDictionary<string, long> _observations = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MeterListener _meterListener;
    private readonly ActivityListener _activityListener;

    internal DurabilityTelemetry()
    {
        _meterListener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (IsKnownSource(instrument.Meter.Name) &&
                    KnownInstruments.Contains(instrument.Name))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _meterListener.SetMeasurementEventCallback<long>(RecordMeasurement);
        _meterListener.SetMeasurementEventCallback<double>(RecordMeasurement);
        _meterListener.Start();

        _activityListener = new ActivityListener
        {
            ShouldListenTo = static source => IsKnownSource(source.Name),
            Sample = static (ref _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = RecordActivity
        };
        ActivitySource.AddActivityListener(_activityListener);
    }

    internal Task Completion => _completed.Task;

    internal string FormatInputMetrics()
        => $"Meter {InputSource}: {InputLease}={Metric(InputLease)}; " +
           $"{InputMessage}{{outcome=delivered}}={Metric($"{InputMessage}|outcome=delivered")}; " +
           $"{InputDuration} observed={Metric(InputDuration)}";

    internal string FormatInputActivities()
        => $"Activity {InputSource}: {InputActivity} kind=Consumer stopped=" +
           Activity($"{InputSource}|{InputActivity}|Consumer");

    internal string FormatOutputMetrics()
        => $"Meter {OutputSource}: " +
           $"{OutputCapture}{{result=enqueued}}={Metric($"{OutputCapture}|result=enqueued")}; " +
           $"{OutputCaptureDuration} observed={Metric(OutputCaptureDuration)}; " +
           $"{OutputLease}={Metric(OutputLease)}; " +
           $"{OutputHandler}{{result=succeeded}}={Metric($"{OutputHandler}|result=succeeded")}; " +
           $"{OutputDelivery}{{outcome=completed,result=applied}}={Metric($"{OutputDelivery}|outcome=completed|result=applied")}; " +
           $"{OutputDeliveryDuration} observed={Metric(OutputDeliveryDuration)}";

    internal string FormatOutputActivities()
        => $"Activity {OutputSource}: " +
           $"{OutputCaptureActivity} kind=Producer outcome=enqueued stopped=" +
           $"{Activity($"{OutputSource}|{OutputCaptureActivity}|Producer|outcome=enqueued")}; " +
           $"{OutputDeliveryActivity} kind=Consumer outcome=completed stopped=" +
           Activity($"{OutputSource}|{OutputDeliveryActivity}|Consumer|outcome=completed");

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    private static bool IsKnownSource(string name)
        => string.Equals(name, InputSource, StringComparison.Ordinal) ||
           string.Equals(name, OutputSource, StringComparison.Ordinal);

    private void RecordMeasurement<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
        where T : struct
    {
        var key = MetricKey(instrument.Name, tags);
        if (key is null)
            return;

        var increment = measurement is long count ? count : 1;
        _observations.AddOrUpdate(key, increment, (_, current) => checked(current + increment));
        TryComplete();
    }

    private void RecordActivity(Activity activity)
    {
        var key = ActivityKey(activity);
        if (key is null)
            return;

        _observations.AddOrUpdate(key, 1, static (_, current) => checked(current + 1));
        TryComplete();
    }

    private void TryComplete()
    {
        if (ExpectedObservationKeys.All(_observations.ContainsKey))
            _completed.TrySetResult();
    }

    private static string? MetricKey(
        string instrument,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (instrument is InputLease or InputDuration or OutputCaptureDuration or
            OutputLease or OutputDeliveryDuration)
        {
            return instrument;
        }

        var outcome = Tag(tags, "outcome");
        var result = Tag(tags, "result");
        return instrument switch
        {
            InputMessage when outcome == "delivered" => $"{instrument}|outcome={outcome}",
            OutputCapture when result == "enqueued" => $"{instrument}|result={result}",
            OutputHandler when result == "succeeded" => $"{instrument}|result={result}",
            OutputDelivery when outcome == "completed" && result == "applied" =>
                $"{instrument}|outcome={outcome}|result={result}",
            _ => null
        };
    }

    private static string? ActivityKey(Activity activity)
    {
        var prefix = $"{activity.Source.Name}|{activity.OperationName}|{activity.Kind}";
        if (activity.OperationName == InputActivity)
            return prefix;

        var outcome = activity.GetTagItem("outcome") as string;
        return activity.OperationName switch
        {
            OutputCaptureActivity when outcome == "enqueued" => $"{prefix}|outcome={outcome}",
            OutputDeliveryActivity when outcome == "completed" => $"{prefix}|outcome={outcome}",
            _ => null
        };
    }

    private static string? Tag(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string name)
    {
        foreach (var tag in tags)
        {
            if (string.Equals(tag.Key, name, StringComparison.Ordinal))
                return tag.Value as string;
        }

        return null;
    }

    private long Metric(string key)
        => Observation(key);

    private long Activity(string key)
        => Observation(key);

    private long Observation(string key)
        => _observations.TryGetValue(key, out var value) ? value : 0;
}
