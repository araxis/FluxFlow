using FluxFlow.Components.Control.Contracts;
using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Components.Control.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Control.Nodes;

public sealed class AssertNode<TInput> : FlowNodeBase
{
    private readonly IFlowExpressionEngine _expressionEngine;
    private readonly IControlContextFactory _contextFactory;
    private readonly ControlNodeContext _nodeContext;
    private readonly ControlExpressionOptions _options;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<ControlAssertionResult> _result;
    private readonly BufferBlock<TInput> _passed;
    private readonly BufferBlock<TInput> _failed;
    private readonly CancellationToken _processingCancellationToken;

    public AssertNode(
        ControlExpressionOptions options,
        IFlowExpressionEngine expressionEngine,
        IControlContextFactory contextFactory,
        ControlNodeContext nodeContext)
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
                "Assert bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true
        };
        _processingCancellationToken = inputOptions.CancellationToken;
        _input = new ActionBlock<TInput>(EvaluateAsync, inputOptions);
        _result = new BufferBlock<ControlAssertionResult>(blockOptions);
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

    public ISourceBlock<ControlAssertionResult> Result => _result;

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
            passed = ControlNodeSupport.Evaluate(
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
                ControlErrorCodes.AssertExpressionFailed,
                $"flow.assert failed to evaluate input: {exception.Message}",
                exception,
                ControlNodeSupport.CreateErrorContext(_options, _expressionEngine));
            TryEmitDiagnostic(
                ControlDiagnosticNames.AssertFailed,
                FlowDiagnosticLevel.Error,
                "flow.assert failed to evaluate input.",
                exception,
                ControlNodeSupport.CreateAttributes(_options, _expressionEngine));
            return;
        }

        var result = new ControlAssertionResult
        {
            Name = _options.EffectiveName,
            Expression = _options.Expression!,
            ExpressionId = _options.ExpressionId,
            ExpressionName = _options.ExpressionName,
            InputType = _options.InputType,
            Passed = passed,
            Value = input,
            Message = passed ? "Assertion passed." : _options.EffectiveFailureMessage
        };

        await _result.SendAsync(result, _processingCancellationToken).ConfigureAwait(false);
        await (passed ? _passed : _failed).SendAsync(input, _processingCancellationToken)
            .ConfigureAwait(false);

        TryEmitDiagnostic(
            ControlDiagnosticNames.AssertEvaluated,
            message: passed ? "flow.assert passed input." : "flow.assert failed input.",
            attributes: ControlNodeSupport.CreateAttributes(_options, _expressionEngine, passed));
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
