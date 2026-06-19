using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Assertions.Nodes;

public sealed class FlowAssertionComponent<TInput> : FlowNodeBase
{
    private readonly IFlowPredicate<TInput> _predicate;
    private readonly AssertionResultMetadata _metadata;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<FlowAssertionResult> _result;
    private readonly BufferBlock<TInput> _passed;
    private readonly BufferBlock<TInput> _failed;

    public FlowAssertionComponent(
        IFlowPredicate<TInput> predicate,
        AssertionResultMetadata metadata)
        : this(predicate, metadata, TimeProvider.System)
    {
    }

    public FlowAssertionComponent(
        IFlowPredicate<TInput> predicate,
        AssertionResultMetadata metadata,
        TimeProvider clock)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (metadata.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metadata),
                "Assertion bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = metadata.BoundedCapacity };
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = metadata.BoundedCapacity,
            EnsureOrdered = true
        };
        _input = new ActionBlock<TInput>(EvaluateAsync, inputOptions);
        _result = new BufferBlock<FlowAssertionResult>(blockOptions);
        _passed = new BufferBlock<TInput>(blockOptions);
        _failed = new BufferBlock<TInput>(blockOptions);
        _input.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(_result.Completion, _passed.Completion, _failed.Completion));
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<FlowAssertionResult> Result => _result;

    public ISourceBlock<TInput> Passed => _passed;

    public ISourceBlock<TInput> Failed => _failed;

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
            ((IDataflowBlock)_result).Fault(exception);
            ((IDataflowBlock)_passed).Fault(exception);
            ((IDataflowBlock)_failed).Fault(exception);
        }
    }

    private async Task EvaluateAsync(TInput input)
    {
        bool passed;
        try
        {
            passed = _predicate.IsMatch(input);
        }
        catch (Exception exception)
        {
            TryReportError(
                AssertionErrorCodes.ExpressionFailed,
                $"flow.assert failed to evaluate input: {exception.Message}",
                exception,
                AssertionNodeSupport.CreateErrorContext(_metadata));
            TryEmitDiagnostic(
                AssertionDiagnosticNames.ExpressionFailed,
                FlowDiagnosticLevel.Error,
                "flow.assert failed to evaluate input.",
                exception,
                AssertionNodeSupport.CreateAttributes(_metadata));
            return;
        }

        var result = CreateResult(input, passed);
        await _result.SendAsync(result).ConfigureAwait(false);
        if (passed && _metadata.EmitPassedInput)
        {
            await _passed.SendAsync(input).ConfigureAwait(false);
        }

        if (!passed && _metadata.EmitFailedInput)
        {
            await _failed.SendAsync(input).ConfigureAwait(false);
        }

        TryEmitDiagnostic(
            AssertionDiagnosticNames.Evaluated,
            message: passed ? "flow.assert passed input." : "flow.assert failed input.",
            attributes: AssertionNodeSupport.CreateAttributes(_metadata, passed));
    }

    private FlowAssertionResult CreateResult(TInput input, bool passed)
    {
        var status = passed
            ? FlowAssertionStatus.Passed
            : FlowAssertionStatus.Failed;
        return new FlowAssertionResult
        {
            Description = _metadata.EffectiveDescription,
            Expression = _metadata.Expression,
            ExpressionId = _metadata.ExpressionId,
            ExpressionName = _metadata.ExpressionName,
            InputType = _metadata.InputType,
            Status = status,
            Message = passed ? "Assertion passed." : _metadata.EffectiveFailureMessage,
            Value = input,
            EvaluatedAt = _clock.GetUtcNow(),
            Failure = passed
                ? null
                : new AssertionFailure
                {
                    Description = _metadata.EffectiveDescription,
                    Message = _metadata.EffectiveFailureMessage,
                    Expression = _metadata.Expression,
                    InputType = _metadata.InputType,
                    Value = input
                }
        };
    }

    private void CompleteOutputs(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_result).Fault(exception);
            ((IDataflowBlock)_passed).Fault(exception);
            ((IDataflowBlock)_failed).Fault(exception);
            return;
        }

        _result.Complete();
        _passed.Complete();
        _failed.Complete();
    }
}
