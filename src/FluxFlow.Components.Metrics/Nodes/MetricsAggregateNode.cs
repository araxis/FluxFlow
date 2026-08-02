using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Diagnostics;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Metrics.Nodes;

/// <summary>
/// Folds ordered metric samples into snapshots and emits successes, partial
/// group-limit outcomes, and expected failures through one normal output.
/// </summary>
public sealed class MetricsAggregateNode : FlowNode<MetricSampleInput, MetricSnapshotOutput>
{
    private const string DefaultGroup = "default";
    private const int MaxTrackedRejectedGroups = 1024;

    private readonly MetricsAggregateOptions _options;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _rateWindow;
    private readonly Queue<DateTimeOffset> _rateSamples = new();
    private readonly Dictionary<string, GroupState> _groups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rejectedGroups = new(StringComparer.Ordinal);
    private FlowMessage<MetricSampleInput>? _lastAcceptedMessage;
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
    public MetricsAggregateNode(
        MetricsAggregateOptions? options = null,
        TimeProvider? clock = null)
        : base(CreateNodeOptions(options ?? new MetricsAggregateOptions()))
    {
        _options = options ?? new MetricsAggregateOptions();
        _clock = clock ?? TimeProvider.System;
        _rateWindow = TimeSpan.FromSeconds(_options.RateWindowSeconds);
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage<MetricSampleInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
        {
            await EmitAsync(
                message.WithError<MetricSnapshotOutput>(message.Error!),
                Stopping).ConfigureAwait(false);
            return;
        }

        var sample = message.Value;
        try
        {
            if (sample is null)
            {
                throw new MetricsOperationException(
                    MetricsErrorCodeNames.InvalidSample,
                    MetricsErrorCodes.InvalidSample,
                    "metrics.aggregate requires a metric sample input.");
            }

            var timestamp = sample.Timestamp ?? _clock.GetUtcNow();
            var value = ResolveValue(sample);
            var size = ResolveSize(sample);
            var groupKey = ResolveGroup(sample);

            _lastAcceptedMessage = message;
            _firstTimestamp ??= timestamp;
            _latestTimestamp = timestamp;
            _sampleCount++;
            _latestName = Normalize(sample.Name);
            _latestUnit = Normalize(sample.Unit);
            if (_options.TrackLatest)
                _latest = CopySample(sample, timestamp);

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
                _totalSize += size.Value;

            var groupLimit = UpdateGroup(groupKey, timestamp, value, size);
            var snapshot = CreateSnapshot(timestamp);
            if (groupLimit is not null)
            {
                await PublishGroupLimitAsync(message, snapshot, groupLimit).ConfigureAwait(false);
            }
            else if (_options.EmitEverySample)
            {
                await EmitAsync(message.With(snapshot), Stopping).ConfigureAwait(false);
            }

            PublishEvent(
                message.CorrelationId,
                _clock.GetUtcNow(),
                MetricsDiagnosticNames.AggregateUpdated,
                FlowEventLevel.Information,
                "metrics.aggregate updated snapshot.",
                MetricsResultKinds.Snapshot,
                snapshot,
                isError: false);
        }
        catch (MetricsOperationException exception)
        {
            await PublishFailureAsync(message, sample, exception).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await PublishFailureAsync(
                message,
                sample,
                new MetricsOperationException(
                    MetricsErrorCodeNames.AggregateFailed,
                    MetricsErrorCodes.AggregateFailed,
                    $"metrics.aggregate failed: {exception.Message}",
                    exception)).ConfigureAwait(false);
        }
    }

    private async Task PublishGroupLimitAsync(
        FlowMessage<MetricSampleInput> message,
        MetricSnapshotOutput snapshot,
        GroupLimitNotice notice)
    {
        var timestamp = _clock.GetUtcNow();
        var details = JsonSerializer.SerializeToElement(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["group"] = notice.Group,
                ["legacyCode"] = MetricsErrorCodes.GroupLimitReached,
                ["maxGroups"] = _options.MaxGroups,
                ["maxTrackedRejectedGroups"] = MaxTrackedRejectedGroups,
                ["rejectedGroupTrackingCapped"] = _rejectedGroupTrackingCapped
            });
        var error = new DataFlowError(
            MetricsErrorCodeNames.GroupLimitReached,
            notice.Message,
            category: "Metrics",
            isTransient: false,
            details);
        await EmitAsync(
            message.WithError<MetricSnapshotOutput>(error),
            Stopping).ConfigureAwait(false);
        PublishEvent(
            message.CorrelationId,
            timestamp,
            MetricsDiagnosticNames.AggregateGroupLimitReached,
            FlowEventLevel.Warning,
            notice.Message,
            MetricsResultKinds.GroupLimitReached,
            snapshot,
            isError: true);
    }

    private async Task PublishFailureAsync(
        FlowMessage<MetricSampleInput> message,
        MetricSampleInput? sample,
        MetricsOperationException exception)
    {
        var timestamp = _clock.GetUtcNow();
        var error = new DataFlowError(
            exception.Code,
            exception.Message,
            category: "Metrics",
            isTransient: false,
            details: CreateErrorDetails(sample, exception));
        await EmitAsync(
            message.WithError<MetricSnapshotOutput>(error),
            Stopping).ConfigureAwait(false);
        PublishEvent(
            message.CorrelationId,
            timestamp,
            MetricsDiagnosticNames.AggregateFailed,
            FlowEventLevel.Warning,
            error.Message,
            MetricsResultKinds.AggregateFailed,
            snapshot: null,
            isError: true);
    }

    private double? ResolveValue(MetricSampleInput sample)
    {
        if (!sample.Value.HasValue)
            return _options.TreatMissingValueAsZero ? 0 : null;

        var value = sample.Value.Value;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new MetricsOperationException(
                MetricsErrorCodeNames.InvalidSample,
                MetricsErrorCodes.InvalidSample,
                "metrics.aggregate sample value must be finite.");
        }

        return value;
    }

    private static long? ResolveSize(MetricSampleInput sample)
    {
        if (!sample.Size.HasValue)
            return null;

        if (sample.Size.Value < 0)
        {
            throw new MetricsOperationException(
                MetricsErrorCodeNames.InvalidSample,
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

    private GroupLimitNotice? UpdateGroup(
        string groupKey,
        DateTimeOffset timestamp,
        double? value,
        long? size)
    {
        if (!_groups.TryGetValue(groupKey, out var group))
        {
            if (_groups.Count >= _options.MaxGroups)
                return CreateGroupLimitNotice(groupKey);

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
            group.TotalSize += size.Value;

        return null;
    }

    private GroupLimitNotice CreateGroupLimitNotice(string groupKey)
    {
        if (!_rejectedGroups.Contains(groupKey))
        {
            if (_rejectedGroups.Count < MaxTrackedRejectedGroups)
                _rejectedGroups.Add(groupKey);
            else
                _rejectedGroupTrackingCapped = true;
        }

        return new GroupLimitNotice(
            groupKey,
            $"metrics.aggregate maxGroups limit reached; group '{groupKey}' was not tracked.");
    }

    private MetricSnapshotOutput CreateSnapshot(DateTimeOffset timestamp)
        => new()
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
            AverageRate = CalculateAverageRate(timestamp),
            TotalSize = _options.TrackSize ? _totalSize : null,
            Latest = _options.TrackLatest ? _latest : null,
            Groups = _groups.ToDictionary(
                group => group.Key,
                group => group.Value.CreateSnapshot(
                    _options,
                    CalculateWindowRate(group.Value.RateSamples, timestamp)),
                StringComparer.Ordinal)
        };

    private async ValueTask PublishFinalSnapshotAsync()
    {
        if (_options.EmitEverySample || !_latestTimestamp.HasValue || _lastAcceptedMessage is null)
            return;

        var snapshot = CreateSnapshot(_latestTimestamp.Value);
        await EmitAsync(
            _lastAcceptedMessage.With(snapshot),
            Stopping).ConfigureAwait(false);
        PublishEvent(
            _lastAcceptedMessage.CorrelationId,
            _clock.GetUtcNow(),
            MetricsDiagnosticNames.AggregateUpdated,
            FlowEventLevel.Information,
            "metrics.aggregate emitted final snapshot.",
            MetricsResultKinds.FinalSnapshot,
            snapshot,
            isError: false);
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
            samples.Dequeue();
    }

    private double CalculateWindowRate(
        Queue<DateTimeOffset> samples,
        DateTimeOffset timestamp)
    {
        TrimRateSamples(samples, timestamp);
        return samples.Count / _rateWindow.TotalSeconds;
    }

    private double CalculateAverageRate(DateTimeOffset timestamp)
    {
        if (!_firstTimestamp.HasValue)
            return 0;

        var seconds = (timestamp - _firstTimestamp.Value).TotalSeconds;
        return seconds <= 0 ? _sampleCount : _sampleCount / seconds;
    }

    private void PublishEvent(
        CorrelationId? correlationId,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string message,
        string resultKind,
        MetricSnapshotOutput? snapshot,
        bool isError)
        => EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = correlationId,
            Name = name,
            Level = level,
            Message = message,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["averageRate"] = snapshot?.AverageRate,
                ["currentRate"] = snapshot?.CurrentRate,
                ["groupCount"] = snapshot?.Groups.Count ?? _groups.Count,
                ["isError"] = isError,
                ["resultKind"] = resultKind,
                ["sampleCount"] = snapshot?.SampleCount ?? _sampleCount,
                ["totalSize"] = snapshot?.TotalSize,
                ["valueCount"] = snapshot?.ValueCount ?? _valueCount
            }
        });

    private static JsonElement CreateErrorDetails(
        MetricSampleInput? sample,
        MetricsOperationException exception)
    {
        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["group"] = sample?.Group,
            ["legacyCode"] = exception.LegacyCode,
            ["name"] = sample?.Name,
            ["size"] = sample?.Size,
            ["value"] = sample?.Value is { } value && double.IsFinite(value)
                ? value
                : sample?.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (exception.InnerException is not null)
        {
            details["exceptionType"] = exception.InnerException.GetType().FullName ??
                exception.InnerException.GetType().Name;
        }

        return JsonSerializer.SerializeToElement(details);
    }

    protected override ValueTask OnInputCompletedAsync()
        => PublishFinalSnapshotAsync();

    private static FlowNodeOptions CreateNodeOptions(MetricsAggregateOptions options)
        => new()
        {
            InputCapacity = options.BoundedCapacity,
            OutputCapacity = options.BoundedCapacity
        };

    private static MetricSampleInput CopySample(
        MetricSampleInput sample,
        DateTimeOffset timestamp)
        => sample with
        {
            Timestamp = timestamp,
            Tags = sample.Tags is null
                ? []
                : new Dictionary<string, string>(sample.Tags, StringComparer.Ordinal)
        };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record GroupLimitNotice(string Group, string Message);

    private sealed record GroupState(string Key)
    {
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

    private sealed class MetricsOperationException(
        string code,
        int legacyCode,
        string message,
        Exception? innerException = null)
        : Exception(message, innerException)
    {
        public string Code { get; } = code;

        public int LegacyCode { get; } = legacyCode;
    }
}
