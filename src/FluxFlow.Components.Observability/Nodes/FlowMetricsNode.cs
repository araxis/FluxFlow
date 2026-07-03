using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Observability.Nodes;

/// <summary>
/// A standalone metrics node. Post a <c>FlowMessage&lt;TInput&gt;</c> to <c>Input</c>;
/// the node tracks count, current/average rate, and optional size, then broadcasts a
/// <c>FlowMessage&lt;FlowMetricSnapshot&gt;</c> on <c>Output</c> carrying the same
/// correlation id. Size-selector failures surface on <c>Errors</c> (with the original
/// correlation id) and the node keeps processing. Diagnostics flow on <c>Events</c>.
/// Works with nothing but <c>new FlowMetricsNode&lt;T&gt;(options)</c> — no engine.
/// </summary>
public sealed class FlowMetricsNode<TInput> : FlowNode<TInput, FlowMetricSnapshot>
{
    public const string NodeType = "flow.metrics";
    public const string Observed = ObservabilityDiagnosticNames.MetricsObserved;
    public const string Failed = ObservabilityDiagnosticNames.MetricsFailed;

    private readonly FlowMetricsOptions _options;
    private readonly IObservabilityValueSelector<TInput>? _sizeSelector;
    private readonly ObservabilityNodeContext _nodeContext;
    private readonly TimeProvider _clock;
    private DateTimeOffset? _firstObservedAt;
    private DateTimeOffset? _previousObservedAt;
    private long _count;
    private long _sizeCount;
    private double? _totalSize;

    public FlowMetricsNode(
        FlowMetricsOptions options,
        IObservabilityValueSelector<TInput>? sizeSelector = null,
        TimeProvider? clock = null)
        : this(ValidateOptions(options), sizeSelector, clock)
    {
    }

    private FlowMetricsNode(
        ValidatedOptions options,
        IObservabilityValueSelector<TInput>? sizeSelector,
        TimeProvider? clock)
        : base(options.FlowNodeOptions)
    {
        _options = options.MetricsOptions;
        _sizeSelector = sizeSelector;
        _clock = clock ?? TimeProvider.System;
        _nodeContext = new ObservabilityNodeContext
        {
            NodeType = NodeType,
            InputType = typeof(TInput),
            Name = _options.EffectiveName
        };
    }

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;
        var observedAt = _clock.GetUtcNow();
        var count = Interlocked.Increment(ref _count);
        _firstObservedAt ??= observedAt;
        var previousObservedAt = _previousObservedAt;
        _previousObservedAt = observedAt;

        var lastSize = TrySelectSize(input, message);
        if (lastSize.HasValue)
        {
            _sizeCount++;
            _totalSize = (_totalSize ?? 0) + lastSize.Value;
        }

        var sizeCount = _sizeCount;
        var totalSize = _totalSize;
        var snapshot = new FlowMetricSnapshot
        {
            Timestamp = observedAt,
            Name = _options.EffectiveName,
            InputType = _options.InputType,
            Count = count,
            LastObservedAt = observedAt,
            CurrentRatePerSecond = CalculateCurrentRate(previousObservedAt, observedAt),
            AverageRatePerSecond = CalculateAverageRate(_firstObservedAt.Value, observedAt, count),
            LastSize = lastSize,
            TotalSize = totalSize,
            AverageSize = totalSize.HasValue && sizeCount > 0 ? totalSize.Value / sizeCount : null
        };

        // Carry the correlation id forward onto the snapshot.
        Emit(message.With(snapshot));
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = Observed,
            Level = FlowEventLevel.Information,
            Message = "flow.metrics observed input.",
            Attributes = ObservabilityNodeSupport.CreateAttributes(
                NodeType,
                _options.InputType,
                _options.EffectiveName,
                count)
        });

        return Task.CompletedTask;
    }

    private double? TrySelectSize(TInput input, FlowMessage<TInput> message)
    {
        if (_sizeSelector is null)
        {
            return null;
        }

        try
        {
            var value = _sizeSelector.Select(input, _nodeContext);
            return ObservabilityNodeSupport.ConvertSize(value);
        }
        catch (Exception exception)
        {
            EmitError(new FlowError
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Code = ObservabilityErrorCodes.MetricsSizeSelectorFailed,
                Message = $"flow.metrics failed to read size: {exception.Message}",
                Context = CreateErrorContext(),
                Exception = exception
            });
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = Failed,
                Level = FlowEventLevel.Error,
                Message = "flow.metrics failed to read size.",
                Attributes = ObservabilityNodeSupport.CreateAttributes(
                    NodeType,
                    _options.InputType,
                    _options.EffectiveName)
            });
            return null;
        }
    }

    private string CreateErrorContext()
    {
        var values = new List<string>
        {
            $"inputType={_options.InputType}",
            $"name={_options.EffectiveName}"
        };

        if (!string.IsNullOrWhiteSpace(_options.SizeSelector))
        {
            values.Add($"sizeSelector={_options.SizeSelector}");
        }

        return string.Join("; ", values);
    }

    private static double CalculateCurrentRate(
        DateTimeOffset? previousObservedAt,
        DateTimeOffset observedAt)
    {
        if (!previousObservedAt.HasValue)
        {
            return 0;
        }

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

    private static ValidatedOptions ValidateOptions(FlowMetricsOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new ArgumentException(
                "flow.metrics option 'inputType' cannot be empty.", nameof(options));
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.metrics option 'boundedCapacity' must be greater than zero.");
        }

        return new ValidatedOptions(options);
    }

    private sealed class ValidatedOptions(FlowMetricsOptions metricsOptions)
    {
        public FlowMetricsOptions MetricsOptions { get; } = metricsOptions;

        public FlowNodeOptions FlowNodeOptions { get; } = new()
        {
            InputCapacity = metricsOptions.BoundedCapacity
        };
    }
}
