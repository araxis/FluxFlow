using System.Diagnostics;

namespace FluxFlow.Engine.Benchmarks.LoadQualification;

/// <summary>Bounded counters only: measures instrumentation plus callbacks, not exporter cost.</summary>
internal sealed class LoadTracing : IDisposable
{
    private readonly ActivityListener? _listener;
    private readonly string _mode;
    private readonly ActivityContext _inputContext;
    private long _samples, _started, _stopped, _fullData, _unexpected, _correlated;
    private int _active, _peak;

    internal LoadTracing(string mode, bool inputTraceContext)
    {
        _mode = mode;
        if (inputTraceContext)
            ActivityContext.TryParse("00-11111111111111111111111111111111-2222222222222222-01",
                "load=qualification", out _inputContext);
        if (mode == "off") return;
        var sampling = mode == "recorded" ? ActivitySamplingResult.AllDataAndRecorded
            : mode == "propagation" ? ActivitySamplingResult.PropagationData : ActivitySamplingResult.None;
        _listener = new()
        {
            ShouldListenTo = source => source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
            {
                Interlocked.Increment(ref _samples);
                return sampling;
            },
            ActivityStarted = activity =>
            {
                Interlocked.Increment(ref _started);
                if (activity.IsAllDataRequested) Interlocked.Increment(ref _fullData);
                if (_inputContext != default && activity.TraceId == _inputContext.TraceId &&
                    activity.ParentSpanId == _inputContext.SpanId) Interlocked.Increment(ref _correlated);
                if (activity.OperationName != "flow.process") Interlocked.Increment(ref _unexpected);
                var active = Interlocked.Increment(ref _active);
                int peak;
                do { peak = Volatile.Read(ref _peak); }
                while (active > peak && Interlocked.CompareExchange(ref _peak, active, peak) != peak);
            },
            ActivityStopped = _ =>
            {
                Interlocked.Increment(ref _stopped);
                Interlocked.Decrement(ref _active);
            }
        };
        ActivitySource.AddActivityListener(_listener);
    }

    internal int Active => Volatile.Read(ref _active);

    internal LoadTraceReport Validate(long expectedOperations)
    {
        var expectedSamples = _mode == "off" ? 0 : expectedOperations;
        var expectedSpans = _mode is "off" or "unsampled" ? 0 : expectedOperations;
        if (_samples != expectedSamples || _started != expectedSpans || _stopped != expectedSpans ||
            _active != 0 || _unexpected != 0 || _fullData != (_mode == "recorded" ? expectedSpans : 0) ||
            _correlated != (_inputContext == default ? 0 : expectedSpans))
            throw new InvalidOperationException("Tracing counts did not settle for the configured load mode.");
        return new(_mode, expectedOperations, _samples, _started, _stopped, _fullData, _correlated, _peak, _active);
    }

    public void Dispose() => _listener?.Dispose();
}

internal sealed record LoadTraceReport(string Mode, long ExpectedOperationsIncludingWarmup,
    long SampleCallbacks, long Started, long Stopped, long FullDataStarted, long CorrelatedStarted, int PeakActive, int ActiveAfterDrain)
{
    public string Scope => "Core processing instrumentation and bounded counter callbacks; includes warmup counts; no retained spans, exporter, backend, metric or log collection";
}
