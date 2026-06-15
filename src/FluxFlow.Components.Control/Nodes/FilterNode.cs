using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Components.Control.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Control.Nodes;

public sealed class FilterNode<TInput> : FlowNodeBase
{
    private readonly IFlowPredicate<TInput> _predicate;
    private readonly string _engineName;
    private readonly ControlExpressionOptions _options;
    private readonly TransformManyBlock<TInput, TInput> _block;

    internal FilterNode(
        ControlExpressionOptions options,
        IFlowPredicate<TInput> predicate,
        string engineName)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _engineName = engineName ?? throw new ArgumentNullException(nameof(engineName));
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
            passed = _predicate.IsMatch(input);
        }
        catch (Exception exception)
        {
            TryReportError(
                ControlErrorCodes.FilterExpressionFailed,
                $"flow.filter failed to evaluate input: {exception.Message}",
                exception,
                ControlNodeSupport.CreateErrorContext(_options, _engineName));
            TryEmitDiagnostic(
                ControlDiagnosticNames.FilterFailed,
                FlowDiagnosticLevel.Error,
                "flow.filter failed to evaluate input.",
                exception,
                ControlNodeSupport.CreateAttributes(_options, _engineName));
            yield break;
        }

        TryEmitDiagnostic(
            passed ? ControlDiagnosticNames.FilterPassed : ControlDiagnosticNames.FilterRejected,
            message: passed ? "flow.filter passed input." : "flow.filter rejected input.",
            attributes: ControlNodeSupport.CreateAttributes(_options, _engineName, passed));

        if (passed)
        {
            yield return input;
        }
    }
}
