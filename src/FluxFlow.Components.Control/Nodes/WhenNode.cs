using FluxFlow.Components.Control.Contracts;
using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Components.Control.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Control.Nodes;

public sealed class WhenNode<TInput> : FlowNodeBase
{
    private readonly IFlowExpressionEngine _expressionEngine;
    private readonly IControlContextFactory _contextFactory;
    private readonly ControlNodeContext _nodeContext;
    private readonly ControlExpressionOptions _options;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<TInput> _whenTrue;
    private readonly BufferBlock<TInput> _whenFalse;
    private readonly CancellationToken _processingCancellationToken;

    public WhenNode(
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
                "When bounded capacity must be greater than zero.");
        }

        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true
        };
        _processingCancellationToken = inputOptions.CancellationToken;
        _input = new ActionBlock<TInput>(RouteAsync, inputOptions);
        _whenTrue = new BufferBlock<TInput>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        _whenFalse = new BufferBlock<TInput>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        _input.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(_whenTrue.Completion, _whenFalse.Completion));
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<TInput> WhenTrue => _whenTrue;

    public ISourceBlock<TInput> WhenFalse => _whenFalse;

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
            ((IDataflowBlock)_whenTrue).Fault(exception);
            ((IDataflowBlock)_whenFalse).Fault(exception);
        }
    }

    private async Task RouteAsync(TInput input)
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
                ControlErrorCodes.WhenExpressionFailed,
                $"flow.when failed to evaluate input: {exception.Message}",
                exception,
                ControlNodeSupport.CreateErrorContext(_options, _expressionEngine));
            TryEmitDiagnostic(
                ControlDiagnosticNames.WhenFailed,
                FlowDiagnosticLevel.Error,
                "flow.when failed to evaluate input.",
                exception,
                ControlNodeSupport.CreateAttributes(_options, _expressionEngine));
            return;
        }

        var route = passed ? ControlComponentPorts.WhenTrue : ControlComponentPorts.WhenFalse;
        TryEmitDiagnostic(
            ControlDiagnosticNames.WhenRouted,
            message: $"flow.when routed input to {route}.",
            attributes: ControlNodeSupport.CreateAttributes(
                _options,
                _expressionEngine,
                passed,
                route));

        var target = passed ? _whenTrue : _whenFalse;
        await target.SendAsync(input, _processingCancellationToken).ConfigureAwait(false);
    }

    private void CompleteOutputs(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_whenTrue).Fault(exception);
            ((IDataflowBlock)_whenFalse).Fault(exception);
            return;
        }

        _whenTrue.Complete();
        _whenFalse.Complete();
    }
}
