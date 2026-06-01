using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Diagnostics;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Assertions.Nodes;

public sealed class FlowAssertionComponent<TInput> : FlowNodeBase
{
    private readonly IFlowExpressionEngine _expressionEngine;
    private readonly IAssertionContextFactory _contextFactory;
    private readonly AssertionNodeContext _nodeContext;
    private readonly AssertionOptions _options;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<FlowAssertionResult> _result;
    private readonly BufferBlock<TInput> _passed;
    private readonly BufferBlock<TInput> _failed;
    private readonly CancellationToken _processingCancellationToken;

    public FlowAssertionComponent(
        AssertionOptions options,
        IFlowExpressionEngine expressionEngine,
        IAssertionContextFactory contextFactory,
        AssertionNodeContext nodeContext)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _expressionEngine = expressionEngine ?? throw new ArgumentNullException(nameof(expressionEngine));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _nodeContext = nodeContext ?? throw new ArgumentNullException(nameof(nodeContext));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Expression);
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Assertion bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true
        };
        _processingCancellationToken = inputOptions.CancellationToken;
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
            _processingCancellationToken.ThrowIfCancellationRequested();
            passed = AssertionNodeSupport.Evaluate(
                _expressionEngine,
                _options,
                _contextFactory,
                _nodeContext,
                input);
        }
        catch (OperationCanceledException) when (_processingCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TryReportError(
                AssertionErrorCodes.ExpressionFailed,
                $"flow.assert failed to evaluate input: {exception.Message}",
                exception,
                AssertionNodeSupport.CreateErrorContext(_options, _expressionEngine));
            TryEmitDiagnostic(
                AssertionDiagnosticNames.ExpressionFailed,
                FlowDiagnosticLevel.Error,
                "flow.assert failed to evaluate input.",
                exception,
                AssertionNodeSupport.CreateAttributes(_options, _expressionEngine));
            return;
        }

        var result = CreateResult(input, passed);
        await _result.SendAsync(result, _processingCancellationToken).ConfigureAwait(false);
        if (passed && _options.EmitPassedInput)
        {
            await _passed.SendAsync(input, _processingCancellationToken).ConfigureAwait(false);
        }

        if (!passed && _options.EmitFailedInput)
        {
            await _failed.SendAsync(input, _processingCancellationToken).ConfigureAwait(false);
        }

        TryEmitDiagnostic(
            AssertionDiagnosticNames.Evaluated,
            message: passed ? "flow.assert passed input." : "flow.assert failed input.",
            attributes: AssertionNodeSupport.CreateAttributes(_options, _expressionEngine, passed));
    }

    private FlowAssertionResult CreateResult(TInput input, bool passed)
    {
        var status = passed
            ? FlowAssertionStatus.Passed
            : FlowAssertionStatus.Failed;
        return new FlowAssertionResult
        {
            Description = _options.EffectiveDescription,
            Expression = _options.Expression!,
            ExpressionId = _options.ExpressionId,
            ExpressionName = _options.ExpressionName,
            InputType = _options.InputType,
            Status = status,
            Message = passed ? "Assertion passed." : _options.EffectiveFailureMessage,
            Value = input,
            Failure = passed
                ? null
                : new AssertionFailure
                {
                    Description = _options.EffectiveDescription,
                    Message = _options.EffectiveFailureMessage,
                    Expression = _options.Expression!,
                    InputType = _options.InputType,
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
