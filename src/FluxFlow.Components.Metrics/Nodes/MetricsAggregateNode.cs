using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Diagnostics;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Metrics.Nodes;

/// <summary>
/// A standalone metrics-aggregation node. Post a
/// <c>FlowMessage&lt;MetricSampleInput&gt;</c> to <c>Input</c>; the node folds each
/// sample into a rolling aggregate and broadcasts a
/// <c>FlowMessage&lt;MetricSnapshotOutput&gt;</c> on <c>Output</c> carrying the same
/// correlation id (rejected-group / invalid-sample failures on <c>Errors</c>,
/// diagnostics on <c>Events</c>). Works with nothing but
/// <c>new MetricsAggregateNode(options, clock)</c> — no engine. Snapshots are
/// broadcast (latest-wins per consumer); when <see cref="MetricsAggregateOptions.EmitEverySample"/>
/// is disabled the node coalesces to a single final snapshot emitted as the input drains.
/// </summary>
public sealed class MetricsAggregateNode : FlowNode<MetricSampleInput, MetricSnapshotOutput>
{
    private const string DefaultGroup = "default";
    private const int MaxTrackedRejectedGroups = 1024;

    private readonly MetricsAggregateOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _rateWindow;
    private readonly Queue<DateTimeOffset> _rateSamples = new();
    private readonly Dictionary<string, GroupState> _groups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rejectedGroups = new(StringComparer.Ordinal);
    private CorrelationId? _lastCorrelationId;
    private DateTimeOffset? _firstTimestamp;
    private DateTimeOffset? _latestTimestamp;
    private bool _rejectedGroupTrackingCapped;
    private MetricSampleInput? _latest;
    private string? _latestName;
    private string? _latestUnit;
    private long _sampleCount;
    private long _valueCount;
    private double _totalValue;
    private double? _minValue;
    private double? _maxValue;
    private long _totalSize;
    private int _finalSnapshotEmitted;

    public MetricsAggregateNode(
        MetricsAggregateOptions? options = null,
        TimeProvider? clock = null)
        : this(ResolveOptions(options), clock, validated: true)
    {
    }

    private MetricsAggregateNode(
        MetricsAggregateOptions options,
        TimeProvider? clock,
        bool validated)
        : base(new FlowNodeOptions
        {
            InputCapacity = options.BoundedCapacity,
            MaxDegreeOfParallelism = 1
        })
    {
        _options = options;
        _timeProvider = clock ?? TimeProvider.System;
        _rateWindow = TimeSpan.FromSeconds(_options.RateWindowSeconds);
    }

    private static MetricsAggregateOptions ResolveOptions(MetricsAggregateOptions? options)
    {
        var resolved = options ?? new MetricsAggregateOptions();
        return resolved;
    }

    protected override Task ProcessAsync(FlowMessage<MetricSampleInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return AggregateAsync(message);
    }

    // In coalesce mode (EmitEverySample = false) the single final snapshot is emitted from
    // the kit's drain hook. The kit guarantees this runs after the input has drained and
    // every ProcessAsync has completed, and before Output is completed — so the final
    // snapshot reliably reaches consumers without racing input-buffer completion (the
    // previous bounded-yield handshake could drop it under scheduling pressure).
    protected override ValueTask OnInputCompletedAsync()
    {
        if (!_options.EmitEverySample)
        {
            TryEmitFinalSnapshot();
        }

        return ValueTask.CompletedTask;
    }

    private Task AggregateAsync(FlowMessage<MetricSampleInput> message)
    {
        var sample = message.Payload;
        try
        {
            _lastCorrelationId = message.CorrelationId;
            var timestamp = sample.Timestamp ?? _timeProvider.GetUtcNow();
            var value = ResolveValue(sample);
            var size = ResolveSize(sample);
            var groupKey = ResolveGroup(sample);

            _firstTimestamp ??= timestamp;
            _latestTimestamp = timestamp;
            _sampleCount++;
            _latestName = Normalize(sample.Name);
            _latestUnit = Normalize(sample.Unit);
            if (_options.TrackLatest)
            {
                _latest = CopySample(sample, timestamp);
            }

            AddRateSample(_rateSamples, timestamp);
            if (value.HasValue)
            {
                _valueCount++;
                _totalValue += value.Value;
                if (_options.TrackMinMax)
                {
                    _minValue = _minValue.HasValue
                        ? Math.Min(_minValue.Value, value.Value)
                        : value.Value;
                    _maxValue = _maxValue.HasValue
                        ? Math.Max(_maxValue.Value, value.Value)
                        : value.Value;
                }
            }

            if (_options.TrackSize && size.HasValue)
            {
                _totalSize += size.Value;
            }

            // Samples whose group was rejected by maxGroups intentionally still update
            // the global aggregates; only the per-group itemization is skipped.
            UpdateGroup(groupKey, timestamp, value, size);
            if (_options.EmitEverySample)
            {
                // Carry the correlation id of the sample that produced this snapshot.
                Emit(message.With(CreateSnapshot(timestamp)));
            }

            TryEmitDiagnostic(
                message.CorrelationId,
                MetricsDiagnosticNames.AggregateUpdated,
                level: FlowEventLevel.Information,
                eventMessage: "metrics.aggregate updated snapshot.",
                attributes: CreateUpdateAttributes(timestamp));
        }
        catch (MetricsAggregateException exception)
        {
            ReportAggregateError(
                exception.Code,
                exception.Message,
                message,
                exception.InnerException);
        }
        catch (Exception exception)
        {
            ReportAggregateError(
                MetricsErrorCodes.AggregateFailed,
                $"metrics.aggregate failed: {exception.Message}",
                message,
                exception);
        }

        return Task.CompletedTask;
    }

    private void TryEmitFinalSnapshot()
    {
        if (_options.EmitEverySample || !_latestTimestamp.HasValue)
        {
            return;
        }

        if (Interlocked.Exchange(ref _finalSnapshotEmitted, 1) != 0)
        {
            return;
        }

        var snapshot = CreateSnapshot(_latestTimestamp.Value);
        var correlationId = _lastCorrelationId ?? CorrelationId.New();
        Emit(new FlowMessage<MetricSnapshotOutput>(correlationId, snapshot));
    }

    private double? ResolveValue(MetricSampleInput sample)
    {
        if (!sample.Value.HasValue)
        {
            return _options.TreatMissingValueAsZero ? 0 : null;
        }

        var value = sample.Value.Value;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new MetricsAggregateException(
                MetricsErrorCodes.InvalidSample,
                "metrics.aggregate sample value must be finite.");
        }

        return value;
    }

    private static long? ResolveSize(MetricSampleInput sample)
    {
        if (!sample.Size.HasValue)
        {
            return null;
        }

        if (sample.Size.Value < 0)
        {
            throw new MetricsAggregateException(
                MetricsErrorCodes.InvalidSample,
                "metrics.aggregate sample size cannot be negative.");
        }

        return sample.Size.Value;
    }

    private string ResolveGroup(MetricSampleInput sample)
    {
        if (!string.IsNullOrWhiteSpace(_options.GroupByTag) &&
            sample.Tags is not null &&
            sample.Tags.TryGetValue(_options.GroupByTag, out var tagValue))
        {
            return Normalize(tagValue) ?? DefaultGroup;
        }

        return Normalize(sample.Group) ?? DefaultGroup;
    }

    private void UpdateGroup(
        string groupKey,
        DateTimeOffset timestamp,
        double? value,
        long? size)
    {
        if (!_groups.TryGetValue(groupKey, out var group))
        {
            if (_groups.Count >= _options.MaxGroups)
            {
                ReportGroupLimit(groupKey);
                return;
            }

            group = new GroupState(groupKey);
            _groups[groupKey] = group;
        }

        group.Count++;
        group.LatestTimestamp = timestamp;
        AddRateSample(group.RateSamples, timestamp);
        if (value.HasValue)
        {
            group.ValueCount++;
            group.TotalValue += value.Value;
            if (_options.TrackMinMax)
            {
                group.MinValue = group.MinValue.HasValue
                    ? Math.Min(group.MinValue.Value, value.Value)
                    : value.Value;
                group.MaxValue = group.MaxValue.HasValue
                    ? Math.Max(group.MaxValue.Value, value.Value)
                    : value.Value;
            }
        }

        if (_options.TrackSize && size.HasValue)
        {
            group.TotalSize += size.Value;
        }
    }

    private void ReportGroupLimit(string groupKey)
    {
        if (_rejectedGroups.Count >= MaxTrackedRejectedGroups)
        {
            if (_rejectedGroups.Contains(groupKey) || _rejectedGroupTrackingCapped)
            {
                return;
            }

            _rejectedGroupTrackingCapped = true;
            var summary =
                $"metrics.aggregate rejected group tracking limit of {MaxTrackedRejectedGroups} reached; " +
                "further rejected groups are not itemized.";
            EmitError(new FlowError
            {
                Timestamp = _timeProvider.GetUtcNow(),
                CorrelationId = _lastCorrelationId,
                Code = MetricsErrorCodes.GroupLimitReached,
                Message = summary,
                Context = $"maxGroups={_options.MaxGroups}; maxTrackedRejectedGroups={MaxTrackedRejectedGroups}"
            });
            EmitDiagnostic(
                _lastCorrelationId,
                MetricsDiagnosticNames.AggregateGroupLimitReached,
                FlowEventLevel.Warning,
                summary,
                attributes: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["maxGroups"] = _options.MaxGroups,
                    ["maxTrackedRejectedGroups"] = MaxTrackedRejectedGroups
                });
            return;
        }

        if (!_rejectedGroups.Add(groupKey))
        {
            return;
        }

        var message = $"metrics.aggregate maxGroups limit reached; group '{groupKey}' was not tracked.";
        EmitError(new FlowError
        {
            Timestamp = _timeProvider.GetUtcNow(),
            CorrelationId = _lastCorrelationId,
            Code = MetricsErrorCodes.GroupLimitReached,
            Message = message,
            Context = $"group={groupKey}; maxGroups={_options.MaxGroups}"
        });
        EmitDiagnostic(
            _lastCorrelationId,
            MetricsDiagnosticNames.AggregateGroupLimitReached,
            FlowEventLevel.Warning,
            message,
            attributes: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["group"] = groupKey,
                ["maxGroups"] = _options.MaxGroups
            });
    }

    private MetricSnapshotOutput CreateSnapshot(DateTimeOffset timestamp)
    {
        var averageRate = CalculateAverageRate(timestamp);
        return new MetricSnapshotOutput
        {
            Timestamp = timestamp,
            Name = _latestName,
            Unit = _latestUnit,
            SampleCount = _sampleCount,
            ValueCount = _valueCount,
            TotalValue = _valueCount == 0 ? null : _totalValue,
            AverageValue = _valueCount == 0 ? null : _totalValue / _valueCount,
            MinValue = _options.TrackMinMax ? _minValue : null,
            MaxValue = _options.TrackMinMax ? _maxValue : null,
            CurrentRate = CalculateWindowRate(_rateSamples, timestamp),
            AverageRate = averageRate,
            TotalSize = _options.TrackSize ? _totalSize : null,
            Latest = _options.TrackLatest ? _latest : null,
            Groups = _groups.ToDictionary(
                group => group.Key,
                group => group.Value.CreateSnapshot(
                    _options,
                    CalculateWindowRate(group.Value.RateSamples, timestamp)),
                StringComparer.Ordinal)
        };
    }

    private void AddRateSample(Queue<DateTimeOffset> samples, DateTimeOffset timestamp)
    {
        samples.Enqueue(timestamp);
        TrimRateSamples(samples, timestamp);
    }

    private void TrimRateSamples(Queue<DateTimeOffset> samples, DateTimeOffset timestamp)
    {
        var cutoff = timestamp - _rateWindow;
        while (samples.TryPeek(out var first) && first < cutoff)
        {
            samples.Dequeue();
        }
    }

    private double CalculateWindowRate(Queue<DateTimeOffset> samples, DateTimeOffset timestamp)
    {
        TrimRateSamples(samples, timestamp);
        return samples.Count / _rateWindow.TotalSeconds;
    }

    private double CalculateAverageRate(DateTimeOffset timestamp)
    {
        if (!_firstTimestamp.HasValue)
        {
            return 0;
        }

        var seconds = (timestamp - _firstTimestamp.Value).TotalSeconds;
        return seconds <= 0 ? _sampleCount : _sampleCount / seconds;
    }

    private void ReportAggregateError(
        int code,
        string message,
        FlowMessage<MetricSampleInput> source,
        Exception? exception)
    {
        var sample = source.Payload;
        EmitError(new FlowError
        {
            Timestamp = _timeProvider.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = CreateErrorContext(sample),
            Exception = exception
        });
        EmitDiagnostic(
            source.CorrelationId,
            MetricsDiagnosticNames.AggregateFailed,
            FlowEventLevel.Error,
            message,
            exception,
            CreateSampleAttributes(sample));
    }

    private void TryEmitDiagnostic(
        CorrelationId correlationId,
        string name,
        FlowEventLevel level,
        string eventMessage,
        IReadOnlyDictionary<string, object?> attributes)
        => EmitDiagnostic(correlationId, name, level, eventMessage, attributes: attributes);

    private void EmitDiagnostic(
        CorrelationId? correlationId,
        string name,
        FlowEventLevel level,
        string? message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? attributes = null)
        => EmitEvent(new FlowEvent
        {
            Timestamp = _timeProvider.GetUtcNow(),
            CorrelationId = correlationId,
            Name = name,
            Level = level,
            Message = message,
            Attributes = attributes ?? new Dictionary<string, object?>(StringComparer.Ordinal)
        });

    private static MetricSampleInput CopySample(MetricSampleInput sample, DateTimeOffset timestamp)
        => sample with
        {
            Timestamp = timestamp,
            Tags = sample.Tags is null
                ? []
                : new Dictionary<string, string>(sample.Tags, StringComparer.Ordinal)
        };

    private Dictionary<string, object?> CreateUpdateAttributes(DateTimeOffset timestamp)
        => new(StringComparer.Ordinal)
        {
            ["sampleCount"] = _sampleCount,
            ["valueCount"] = _valueCount,
            ["groupCount"] = _groups.Count,
            ["currentRate"] = CalculateWindowRate(_rateSamples, timestamp),
            ["averageRate"] = CalculateAverageRate(timestamp),
            ["totalSize"] = _options.TrackSize ? _totalSize : (long?)null
        };

    private static Dictionary<string, object?> CreateSampleAttributes(MetricSampleInput sample)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = sample.Name,
            ["group"] = sample.Group,
            ["hasValue"] = sample.Value.HasValue,
            ["size"] = sample.Size,
            ["tagCount"] = sample.Tags?.Count ?? 0
        };
        return attributes;
    }

    private static string CreateErrorContext(MetricSampleInput sample)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(sample.Name))
        {
            values.Add($"name={sample.Name}");
        }

        if (!string.IsNullOrWhiteSpace(sample.Group))
        {
            values.Add($"group={sample.Group}");
        }

        if (sample.Value.HasValue)
        {
            values.Add($"value={sample.Value.Value}");
        }

        if (sample.Size.HasValue)
        {
            values.Add($"size={sample.Size.Value}");
        }

        return string.Join("; ", values);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class GroupState(string key)
    {
        public string Key { get; } = key;
        public long Count { get; set; }
        public long ValueCount { get; set; }
        public double TotalValue { get; set; }
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public long TotalSize { get; set; }
        public DateTimeOffset LatestTimestamp { get; set; }
        public Queue<DateTimeOffset> RateSamples { get; } = new();

        public MetricGroupSnapshot CreateSnapshot(
            MetricsAggregateOptions options,
            double currentRate)
            => new()
            {
                Group = Key,
                Count = Count,
                ValueCount = ValueCount,
                TotalValue = ValueCount == 0 ? null : TotalValue,
                AverageValue = ValueCount == 0 ? null : TotalValue / ValueCount,
                MinValue = options.TrackMinMax ? MinValue : null,
                MaxValue = options.TrackMinMax ? MaxValue : null,
                CurrentRate = currentRate,
                TotalSize = options.TrackSize ? TotalSize : null,
                LatestTimestamp = LatestTimestamp
            };
    }

    private sealed class MetricsAggregateException(
        int code,
        string message,
        Exception? innerException = null)
        : Exception(message, innerException)
    {
        public int Code { get; } = code;
    }
}
