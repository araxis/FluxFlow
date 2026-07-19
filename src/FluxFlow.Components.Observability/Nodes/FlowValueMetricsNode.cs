using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Observability.Nodes;

/// <summary>
/// Observes immutable workflow values and emits complete, partial, and expected
/// failure metric outcomes through one normal result output.
/// </summary>
public sealed class FlowValueMetricsNode : IFlowNode
{
    private const string InputType = nameof(FlowValue);

    private readonly FlowValueMetricsOptions _options;
    private readonly IObservabilityFlowValueSelector? _sizeSelector;
    private readonly ObservabilityNodeContext _nodeContext;
    private readonly TimeProvider _clock;
    private readonly FlowValueObservabilityPipeline<FlowMetricSnapshot> _pipeline;
    private DateTimeOffset? _firstObservedAt;
    private DateTimeOffset? _previousObservedAt;
    private long _count;
    private long _sizeCount;
    private double? _totalSize;

    public FlowValueMetricsNode(
        FlowValueMetricsOptions options,
        IObservabilityFlowValueSelector? sizeSelector = null,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options);
        _sizeSelector = sizeSelector;
        _clock = clock ?? TimeProvider.System;
        _nodeContext = new ObservabilityNodeContext
        {
            NodeType = FlowMetricsNode<FlowValue>.NodeType,
            InputType = typeof(FlowValue),
            Name = _options.EffectiveName
        };
        _pipeline = new FlowValueObservabilityPipeline<FlowMetricSnapshot>(
            _options.BoundedCapacity,
            Process);
    }

    public ITargetBlock<FlowMessage<FlowValue>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<FlowMetricSnapshot>>> Output
        => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private FlowMessage<FlowResult<FlowMetricSnapshot>> Process(
        FlowMessage<FlowValue> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var timestamp = _clock.GetUtcNow();
        if (message.Payload is null)
        {
            return Failure(
                message,
                timestamp,
                ObservabilityResultKinds.MetricsFailed,
                ObservabilityErrorCodeNames.MissingInput,
                "flow.metrics requires FlowValue input.");
        }

        try
        {
            var count = ++_count;
            _firstObservedAt ??= timestamp;
            var previousObservedAt = _previousObservedAt;
            _previousObservedAt = timestamp;

            double? lastSize = null;
            Exception? sizeException = null;
            if (_sizeSelector is not null)
            {
                try
                {
                    lastSize = ConvertSize(_sizeSelector.Select(message.Payload, _nodeContext));
                }
                catch (Exception exception)
                {
                    sizeException = exception;
                }
            }

            if (lastSize.HasValue)
            {
                _sizeCount++;
                _totalSize = (_totalSize ?? 0) + lastSize.Value;
            }

            var snapshot = new FlowMetricSnapshot
            {
                Timestamp = timestamp,
                Name = _options.EffectiveName,
                InputType = InputType,
                Count = count,
                LastObservedAt = timestamp,
                CurrentRatePerSecond = CalculateCurrentRate(previousObservedAt, timestamp),
                AverageRatePerSecond = CalculateAverageRate(
                    _firstObservedAt.Value,
                    timestamp,
                    count),
                LastSize = lastSize,
                TotalSize = _totalSize,
                AverageSize = _totalSize.HasValue && _sizeCount > 0
                    ? _totalSize.Value / _sizeCount
                    : null
            };
            if (sizeException is null)
            {
                PublishEvent(
                    message,
                    timestamp,
                    ObservabilityDiagnosticNames.MetricsObserved,
                    "flow.metrics observed input.",
                    ObservabilityResultKinds.MetricSnapshot,
                    snapshot,
                    isError: false);
                return message.With(FlowResult<FlowMetricSnapshot>.Success(
                    ObservabilityResultKinds.MetricSnapshot,
                    snapshot,
                    timestamp));
            }

            var error = new DataFlowError(
                ObservabilityErrorCodeNames.MetricsSizeSelectorFailed,
                $"flow.metrics failed to read size: {sizeException.Message}",
                category: "Observability.Metrics",
                isTransient: false,
                details: CreateErrorDetails(message.Payload, sizeException));
            PublishEvent(
                message,
                timestamp,
                ObservabilityDiagnosticNames.MetricsFailed,
                error.Message,
                ObservabilityResultKinds.MetricSnapshotPartial,
                snapshot,
                isError: true);
            return message.With(FlowResult<FlowMetricSnapshot>.Failure(
                ObservabilityResultKinds.MetricSnapshotPartial,
                error,
                timestamp,
                snapshot));
        }
        catch (Exception exception)
        {
            return Failure(
                message,
                timestamp,
                ObservabilityResultKinds.MetricsFailed,
                ObservabilityErrorCodeNames.MetricsFailed,
                $"flow.metrics failed to observe input: {exception.Message}",
                exception);
        }
    }

    private FlowMessage<FlowResult<FlowMetricSnapshot>> Failure(
        FlowMessage<FlowValue> message,
        DateTimeOffset timestamp,
        string resultKind,
        string errorCode,
        string errorMessage,
        Exception? exception = null)
    {
        var error = new DataFlowError(
            errorCode,
            errorMessage,
            category: "Observability.Metrics",
            isTransient: false,
            details: CreateErrorDetails(message.Payload, exception));
        PublishEvent(
            message,
            timestamp,
            ObservabilityDiagnosticNames.MetricsFailed,
            error.Message,
            resultKind,
            snapshot: null,
            isError: true);
        return message.With(FlowResult<FlowMetricSnapshot>.Failure(
            resultKind,
            error,
            timestamp));
    }

    private void PublishEvent(
        FlowMessage<FlowValue> message,
        DateTimeOffset timestamp,
        string name,
        string text,
        string resultKind,
        FlowMetricSnapshot? snapshot,
        bool isError)
        => _pipeline.PublishEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = isError ? FlowEventLevel.Warning : FlowEventLevel.Information,
            Message = text,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["count"] = snapshot?.Count ?? _count,
                ["inputType"] = InputType,
                ["isError"] = isError,
                ["name"] = _options.EffectiveName,
                ["nodeType"] = FlowMetricsNode<FlowValue>.NodeType,
                ["resultKind"] = resultKind,
                ["totalSize"] = snapshot?.TotalSize ?? _totalSize
            }
        });

    private FlowValue CreateErrorDetails(FlowValue? input, Exception? exception)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["input"] = input ?? FlowValue.Null,
            ["name"] = FlowValue.From(_options.EffectiveName)
        };
        if (exception is not null)
        {
            details["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name);
        }

        return FlowValue.FromObject(details);
    }

    private static double? ConvertSize(FlowValue? value)
    {
        double? size = value?.Kind switch
        {
            null or FlowValueKind.Null => null,
            FlowValueKind.Integer => (double)value.GetInteger(),
            FlowValueKind.Decimal => (double)value.GetDecimal(),
            FlowValueKind.FloatingPoint => value.GetFloatingPoint(),
            FlowValueKind.String => value.GetString().Length,
            FlowValueKind.Binary => value.GetBinary().Length,
            FlowValueKind.Array => value.GetArray().Length,
            FlowValueKind.Object => value.GetObject().Count,
            _ => throw new InvalidOperationException(
                $"Size selector returned {value.Kind}; expected a number, string, binary, array, object, or null.")
        };

        if (size.HasValue &&
            (double.IsNaN(size.Value) || double.IsInfinity(size.Value) || size.Value < 0))
        {
            throw new InvalidOperationException(
                "Size selector returned a non-finite or negative value.");
        }

        return size;
    }

    private static double CalculateCurrentRate(
        DateTimeOffset? previousObservedAt,
        DateTimeOffset observedAt)
    {
        if (!previousObservedAt.HasValue)
            return 0;

        var seconds = (observedAt - previousObservedAt.Value).TotalSeconds;
        return seconds <= 0 ? 0 : 1 / seconds;
    }

    private static double CalculateAverageRate(
        DateTimeOffset firstObservedAt,
        DateTimeOffset observedAt,
        long count)
    {
        var seconds = (observedAt - firstObservedAt).TotalSeconds;
        return seconds <= 0 ? count : count / seconds;
    }

    private static FlowValueMetricsOptions ValidateOptions(
        FlowValueMetricsOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.metrics option 'boundedCapacity' must be greater than zero.");
        }

        return options;
    }
}
