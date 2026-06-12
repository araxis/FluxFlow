using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Diagnostics;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Components.Metrics.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Metrics.Nodes;

public sealed class MetricsAggregateNode : FlowNodeBase
{
    private const string DefaultGroup = "default";
    private const int MaxTrackedRejectedGroups = 1024;

    private readonly MetricsAggregateOptions _options;
    private readonly IMetricsClock _clock;
    private readonly TimeSpan _rateWindow;
    private readonly ActionBlock<MetricSampleInput> _input;
    private readonly BufferBlock<MetricSnapshotOutput> _output;
    private readonly Queue<DateTimeOffset> _rateSamples = new();
    private readonly Dictionary<string, GroupState> _groups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rejectedGroups = new(StringComparer.Ordinal);
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

    private MetricsAggregateNode(
        MetricsAggregateOptions options,
        IMetricsClock clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "metrics.aggregate bounded capacity must be greater than zero.");
        }

        _rateWindow = TimeSpan.FromSeconds(options.RateWindowSeconds);
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _input = new ActionBlock<MetricSampleInput>(AggregateAsync, inputOptions);
        _output = new BufferBlock<MetricSnapshotOutput>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        CompleteWhen(_input.Completion);
    }

    public ITargetBlock<MetricSampleInput> Input => _input;

    public ISourceBlock<MetricSnapshotOutput> Output => _output;

    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        => Create(context, new MetricsComponentOptions());

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        MetricsComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = MetricsOptionsReader.ReadAggregateOptions(context.Definition);
        var node = new MetricsAggregateNode(options, componentOptions.Clock);

        return context.CreateNode(node)
            .Input(MetricsComponentPorts.Input, node.Input)
            .Output(MetricsComponentPorts.Output, node.Output)
            .Output(MetricsComponentPorts.Errors, node.Errors)
            .Build();
    }

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
            ((IDataflowBlock)_output).Fault(exception);
        }
    }

    protected override void OnNodeCompleted()
    {
        if (!_options.EmitEverySample && _latestTimestamp.HasValue)
        {
            EmitSnapshot(CreateSnapshot(_latestTimestamp.Value));
        }

        _output.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        ((IDataflowBlock)_output).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    private Task AggregateAsync(MetricSampleInput sample)
    {
        try
        {
            var timestamp = sample.Timestamp ?? _clock.UtcNow;
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
                EmitSnapshot(CreateSnapshot(timestamp));
            }

            TryEmitDiagnostic(
                MetricsDiagnosticNames.AggregateUpdated,
                message: "metrics.aggregate updated snapshot.",
                attributes: CreateUpdateAttributes(timestamp));
        }
        catch (MetricsAggregateException exception)
        {
            ReportAggregateError(
                exception.Code,
                exception.Message,
                sample,
                exception.InnerException);
        }
        catch (Exception exception)
        {
            ReportAggregateError(
                MetricsErrorCodes.AggregateFailed,
                $"metrics.aggregate failed: {exception.Message}",
                sample,
                exception);
        }

        return Task.CompletedTask;
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
            TryReportError(
                MetricsErrorCodes.GroupLimitReached,
                summary,
                context: $"maxGroups={_options.MaxGroups}; maxTrackedRejectedGroups={MaxTrackedRejectedGroups}");
            TryEmitDiagnostic(
                MetricsDiagnosticNames.AggregateGroupLimitReached,
                FlowDiagnosticLevel.Warning,
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
        TryReportError(
            MetricsErrorCodes.GroupLimitReached,
            message,
            context: $"group={groupKey}; maxGroups={_options.MaxGroups}");
        TryEmitDiagnostic(
            MetricsDiagnosticNames.AggregateGroupLimitReached,
            FlowDiagnosticLevel.Warning,
            message,
            attributes: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["group"] = groupKey,
                ["maxGroups"] = _options.MaxGroups
            });
    }

    private void EmitSnapshot(MetricSnapshotOutput snapshot)
    {
        if (_output.Post(snapshot))
        {
            return;
        }

        var message = "metrics.aggregate snapshot output was full; snapshot was dropped.";
        TryReportError(
            MetricsErrorCodes.SnapshotDropped,
            message,
            context: $"sampleCount={snapshot.SampleCount}; boundedCapacity={_options.BoundedCapacity}");
        TryEmitDiagnostic(
            MetricsDiagnosticNames.AggregateSnapshotDropped,
            FlowDiagnosticLevel.Warning,
            message,
            attributes: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sampleCount"] = snapshot.SampleCount,
                ["boundedCapacity"] = _options.BoundedCapacity
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
        MetricSampleInput sample,
        Exception? exception)
    {
        TryReportError(code, message, exception, CreateErrorContext(sample));
        TryEmitDiagnostic(
            MetricsDiagnosticNames.AggregateFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CreateSampleAttributes(sample));
    }

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
