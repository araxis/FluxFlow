using FluxFlow.Components.Control.Contracts;
using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Components.Control.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Control.Nodes;

public sealed class FilterNode<TInput> : FlowNodeBase
{
    private readonly IFlowExpressionEngine _expressionEngine;
    private readonly IControlContextFactory _contextFactory;
    private readonly ControlNodeContext _nodeContext;
    private readonly ControlExpressionOptions _options;
    private readonly TransformManyBlock<TInput, TInput> _block;
    private readonly CancellationToken _processingCancellationToken;

    public FilterNode(
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
                "Filter bounded capacity must be greater than zero.");
        }

        var blockOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true
        };
        _processingCancellationToken = blockOptions.CancellationToken;
        _block = new TransformManyBlock<TInput, TInput>(Filter, blockOptions);
        CompleteWhen(_block.Completion);
    }

    public ITargetBlock<TInput> Input => _block;

    public ISourceBlock<TInput> Output => _block;

    public override void Complete()
        => _block.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_block).Fault(exception);
        }
    }

    private IEnumerable<TInput> Filter(TInput input)
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
                ControlErrorCodes.FilterExpressionFailed,
                $"flow.filter failed to evaluate input: {exception.Message}",
                exception,
                ControlNodeSupport.CreateErrorContext(_options, _expressionEngine));
            TryEmitDiagnostic(
                ControlDiagnosticNames.FilterFailed,
                FlowDiagnosticLevel.Error,
                "flow.filter failed to evaluate input.",
                exception,
                ControlNodeSupport.CreateAttributes(_options, _expressionEngine));
            yield break;
        }

        TryEmitDiagnostic(
            passed ? ControlDiagnosticNames.FilterPassed : ControlDiagnosticNames.FilterRejected,
            message: passed ? "flow.filter passed input." : "flow.filter rejected input.",
            attributes: ControlNodeSupport.CreateAttributes(_options, _expressionEngine, passed));

        if (passed)
        {
            yield return input;
        }
    }
}
