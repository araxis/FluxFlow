using System.Collections;
using System.Text.Json;
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
public sealed class FlowMetricsNode : FlowMetricsNode<JsonElement>
{
    public FlowMetricsNode(
        FlowMetricsOptions options,
        IObservabilityValueSelector<JsonElement>? sizeSelector = null,
        TimeProvider? clock = null)
        : base(options, sizeSelector, clock)
    {
    }
}

public class FlowMetricsNode<T> : IFlowNode
{
    private const string ComponentType = "metric.measure";

    private readonly FlowMetricsOptions _options;
    private readonly IObservabilityValueSelector<T>? _sizeSelector;
    private readonly ObservabilityNodeContext _nodeContext;
    private readonly TimeProvider _clock;
    private readonly ObservabilityPipeline<T, FlowMetricSnapshot> _pipeline;
    private DateTimeOffset? _firstObservedAt;
    private DateTimeOffset? _previousObservedAt;
    private long _count;
    private long _sizeCount;
    private double? _totalSize;

    public FlowMetricsNode(
        FlowMetricsOptions options,
        IObservabilityValueSelector<T>? sizeSelector = null,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options);
        _sizeSelector = sizeSelector;
        _clock = clock ?? TimeProvider.System;
        _nodeContext = new ObservabilityNodeContext
        {
            NodeType = ComponentType,
            InputType = typeof(T),
            Name = _options.EffectiveName
        };
        _pipeline = new ObservabilityPipeline<T, FlowMetricSnapshot>(
            _options.BoundedCapacity,
            Process);
    }

    public ITargetBlock<FlowMessage<T>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowMetricSnapshot>> Output
        => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private FlowMessage<FlowMetricSnapshot> Process(FlowMessage<T> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError<FlowMetricSnapshot>(message.Error!);

        var timestamp = _clock.GetUtcNow();
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
                    lastSize = ConvertSize(_sizeSelector.Select(message.Value, _nodeContext));
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
                InputType = typeof(T).FullName ?? typeof(T).Name,
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
                return message.With(snapshot);
            }

            var error = new DataFlowError(
                ObservabilityErrorCodeNames.MetricsSizeSelectorFailed,
                $"flow.metrics failed to read size: {sizeException.Message}",
                category: "Observability.Metrics",
                isTransient: false,
                details: CreateErrorDetails(sizeException));
            PublishEvent(
                message,
                timestamp,
                ObservabilityDiagnosticNames.MetricsFailed,
                error.Message,
                ObservabilityResultKinds.MetricSnapshotPartial,
                snapshot,
                isError: true);
            return message.WithError<FlowMetricSnapshot>(error);
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

    private FlowMessage<FlowMetricSnapshot> Failure(
        FlowMessage<T> message,
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
            details: CreateErrorDetails(exception));
        PublishEvent(
            message,
            timestamp,
            ObservabilityDiagnosticNames.MetricsFailed,
            error.Message,
            resultKind,
            snapshot: null,
            isError: true);
        return message.WithError<FlowMetricSnapshot>(error);
    }

    private void PublishEvent(
        FlowMessage<T> message,
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
                ["inputType"] = typeof(T).FullName ?? typeof(T).Name,
                ["isError"] = isError,
                ["name"] = _options.EffectiveName,
                ["nodeType"] = ComponentType,
                ["resultKind"] = resultKind,
                ["totalSize"] = snapshot?.TotalSize ?? _totalSize
            }
        });

    private JsonElement CreateErrorDetails(Exception? exception)
    {
        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = _options.EffectiveName
        };
        if (exception is not null)
        {
            details["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name;
        }

        return JsonSerializer.SerializeToElement(details);
    }

    private static double? ConvertSize(object? value)
    {
        double? size = value switch
        {
            null => null,
            byte value8 => value8,
            short value16 => value16,
            int value32 => value32,
            long value64 => value64,
            float single => single,
            double number => number,
            decimal number => (double)number,
            string text => text.Length,
            byte[] bytes => bytes.Length,
            JsonElement json => JsonSize(json),
            ICollection collection => collection.Count,
            _ => throw new InvalidOperationException(
                $"Size selector returned {value.GetType().Name}; expected a number, string, binary, collection, JSON value, or null.")
        };

        if (size.HasValue &&
            (double.IsNaN(size.Value) || double.IsInfinity(size.Value) || size.Value < 0))
        {
            throw new InvalidOperationException(
                "Size selector returned a non-finite or negative value.");
        }

        return size;
    }

    private static double? JsonSize(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String => value.GetString()?.Length ?? 0,
            JsonValueKind.Array => value.GetArrayLength(),
            JsonValueKind.Object => value.EnumerateObject().Count(),
            _ => throw new InvalidOperationException(
                $"Size selector returned JSON {value.ValueKind}; expected a number, string, array, object, or null.")
        };

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

    private static FlowMetricsOptions ValidateOptions(
        FlowMetricsOptions? options)
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
