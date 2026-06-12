using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Components.Observability.Timing;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Observability.Nodes;

public sealed class FlowMetricsNode<TInput> : FlowNodeBase
{
    private readonly FlowMetricsOptions _options;
    private readonly ObservabilityComponentOptions.IValueSelector? _sizeSelector;
    private readonly ObservabilityNodeContext _nodeContext;
    private readonly IObservabilityClock _clock;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<FlowMetricSnapshot> _snapshots;
    private DateTimeOffset? _firstObservedAt;
    private DateTimeOffset? _previousObservedAt;
    private long _count;
    private long _sizeCount;
    private double? _totalSize;

    internal FlowMetricsNode(
        FlowMetricsOptions options,
        ObservabilityComponentOptions.IValueSelector? sizeSelector,
        ObservabilityNodeContext nodeContext)
        : this(options, sizeSelector, nodeContext, SystemObservabilityClock.Instance)
    {
    }

    internal FlowMetricsNode(
        FlowMetricsOptions options,
        ObservabilityComponentOptions.IValueSelector? sizeSelector,
        ObservabilityNodeContext nodeContext,
        IObservabilityClock clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sizeSelector = sizeSelector;
        _nodeContext = nodeContext ?? throw new ArgumentNullException(nameof(nodeContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Metrics bounded capacity must be greater than zero.");
        }

        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _input = new ActionBlock<TInput>(ObserveAsync, inputOptions);
        _snapshots = new BufferBlock<FlowMetricSnapshot>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        _input.Completion.ContinueWith(
            CompleteOutput,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(_snapshots.Completion);
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<FlowMetricSnapshot> Snapshots => _snapshots;

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
            ((IDataflowBlock)_snapshots).Fault(exception);
        }
    }

    private async Task ObserveAsync(TInput input)
    {
        var observedAt = _clock.UtcNow;
        var count = Interlocked.Increment(ref _count);
        _firstObservedAt ??= observedAt;
        var previousObservedAt = _previousObservedAt;
        _previousObservedAt = observedAt;

        var lastSize = TrySelectSize(input);
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

        await _snapshots.SendAsync(snapshot).ConfigureAwait(false);
        TryEmitDiagnostic(
            ObservabilityDiagnosticNames.MetricsObserved,
            message: "flow.metrics observed input.",
            attributes: ObservabilityNodeSupport.CreateAttributes(
                ObservabilityComponentTypes.Metrics.Value,
                _options.InputType,
                _options.EffectiveName,
                count));
    }

    private double? TrySelectSize(TInput input)
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
            TryReportError(
                ObservabilityErrorCodes.MetricsSizeSelectorFailed,
                $"flow.metrics failed to read size: {exception.Message}",
                exception,
                CreateErrorContext());
            TryEmitDiagnostic(
                ObservabilityDiagnosticNames.MetricsFailed,
                FlowDiagnosticLevel.Error,
                "flow.metrics failed to read size.",
                exception,
                ObservabilityNodeSupport.CreateAttributes(
                    ObservabilityComponentTypes.Metrics.Value,
                    _options.InputType,
                    _options.EffectiveName));
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

    private void CompleteOutput(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_snapshots).Fault(exception);
            return;
        }

        _snapshots.Complete();
    }
}
