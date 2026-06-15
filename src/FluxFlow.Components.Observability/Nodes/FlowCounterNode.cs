using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Observability.Nodes;

public sealed class FlowCounterNode<TInput> : FlowNodeBase
{
    private readonly FlowCounterOptions _options;
    private readonly IFlowPredicate<TInput>? _acceptPredicate;
    private readonly string? _engineName;
    private readonly TimeProvider _timeProvider;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<FlowCounterSnapshot> _snapshots;
    private long _count;
    private long _rejectedCount;

    internal FlowCounterNode(
        FlowCounterOptions options,
        IFlowPredicate<TInput>? acceptPredicate,
        string? engineName)
        : this(options, acceptPredicate, engineName, TimeProvider.System)
    {
    }

    internal FlowCounterNode(
        FlowCounterOptions options,
        IFlowPredicate<TInput>? acceptPredicate,
        string? engineName,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _acceptPredicate = acceptPredicate;
        _engineName = engineName;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Counter bounded capacity must be greater than zero.");
        }

        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _input = new ActionBlock<TInput>(ObserveAsync, inputOptions);
        _snapshots = new BufferBlock<FlowCounterSnapshot>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        _input.Completion.ContinueWith(
            CompleteOutput,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(_snapshots.Completion);
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<FlowCounterSnapshot> Snapshots => _snapshots;

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
        if (!IsAccepted(input))
        {
            return;
        }

        var observedAt = _timeProvider.GetUtcNow();
        var count = Interlocked.Increment(ref _count);
        var snapshot = new FlowCounterSnapshot
        {
            Timestamp = observedAt,
            Name = _options.EffectiveName,
            InputType = _options.InputType,
            Count = count,
            RejectedCount = Volatile.Read(ref _rejectedCount),
            LastObservedAt = observedAt
        };

        await _snapshots.SendAsync(snapshot).ConfigureAwait(false);
        TryEmitDiagnostic(
            ObservabilityDiagnosticNames.CounterIncremented,
            message: "flow.counter incremented.",
            attributes: ObservabilityNodeSupport.CreateAttributes(
                ObservabilityComponentTypes.Counter.Value,
                _options.InputType,
                _options.EffectiveName,
                count));
    }

    private bool IsAccepted(TInput input)
    {
        if (_acceptPredicate is null)
        {
            return true;
        }

        try
        {
            var accepted = _acceptPredicate.IsMatch(input);

            if (!accepted)
            {
                var rejected = Interlocked.Increment(ref _rejectedCount);
                TryEmitDiagnostic(
                    ObservabilityDiagnosticNames.CounterRejected,
                    message: "flow.counter rejected input.",
                    attributes: ObservabilityNodeSupport.CreateAttributes(
                        ObservabilityComponentTypes.Counter.Value,
                        _options.InputType,
                        _options.EffectiveName,
                        rejected));
            }

            return accepted;
        }
        catch (Exception exception)
        {
            TryReportError(
                ObservabilityErrorCodes.CounterPredicateFailed,
                $"flow.counter failed to evaluate input: {exception.Message}",
                exception,
                ObservabilityNodeSupport.CreateExpressionContext(
                    _options,
                    _engineName));
            TryEmitDiagnostic(
                ObservabilityDiagnosticNames.CounterFailed,
                FlowDiagnosticLevel.Error,
                "flow.counter failed to evaluate input.",
                exception,
                ObservabilityNodeSupport.CreateAttributes(
                    ObservabilityComponentTypes.Counter.Value,
                    _options.InputType,
                    _options.EffectiveName));
            return false;
        }
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
