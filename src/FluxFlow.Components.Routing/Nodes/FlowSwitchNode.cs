using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

public sealed class FlowSwitchNode<TInput> : FlowNodeBase
{
    private readonly IFlowExpressionEngine _expressionEngine;
    private readonly IRoutingContextFactory _contextFactory;
    private readonly RoutingNodeContext _nodeContext;
    private readonly SwitchRoutingOptions _options;
    private readonly HashSet<string> _routes;
    private readonly Dictionary<string, string> _routeOutputPorts;
    private readonly Dictionary<string, BufferBlock<TInput>> _routeOutputBlocks;
    private readonly IReadOnlyDictionary<string, ISourceBlock<TInput>> _routeOutputs;
    private readonly ActionBlock<TInput> _input;
    private readonly BufferBlock<FlowSwitchResult<TInput>> _result;
    private readonly BufferBlock<FlowRoute<TInput>> _routed;
    private readonly BufferBlock<TInput> _matched;
    private readonly BufferBlock<TInput> _default;
    private readonly CancellationToken _processingCancellationToken;

    public FlowSwitchNode(
        SwitchRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        IRoutingContextFactory contextFactory,
        RoutingNodeContext nodeContext)
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
                "flow.switch bounded capacity must be greater than zero.");
        }

        _routes = RoutingNodeSupport.CreateRouteSet(options);
        _routeOutputPorts = RoutingNodeSupport.CreateRouteOutputPortMap(options);
        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };
        _routeOutputBlocks = _routeOutputPorts.Values
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                portName => portName,
                _ => new BufferBlock<TInput>(blockOptions),
                StringComparer.Ordinal);
        _routeOutputs = _routeOutputBlocks.ToDictionary(
            routeOutput => routeOutput.Key,
            routeOutput => (ISourceBlock<TInput>)routeOutput.Value,
            StringComparer.Ordinal);
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true
        };
        _processingCancellationToken = inputOptions.CancellationToken;
        _input = new ActionBlock<TInput>(RouteAsync, inputOptions);
        _result = new BufferBlock<FlowSwitchResult<TInput>>(blockOptions);
        _routed = new BufferBlock<FlowRoute<TInput>>(blockOptions);
        _matched = new BufferBlock<TInput>(blockOptions);
        _default = new BufferBlock<TInput>(blockOptions);
        _input.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(
            _routeOutputBlocks.Values
                .Select(output => output.Completion)
                .Append(_routed.Completion)
                .Append(_result.Completion)
                .Append(_matched.Completion)
                .Append(_default.Completion)));
    }

    public ITargetBlock<TInput> Input => _input;

    public ISourceBlock<FlowSwitchResult<TInput>> Result => _result;

    public ISourceBlock<FlowRoute<TInput>> Routed => _routed;

    public ISourceBlock<TInput> Matched => _matched;

    public ISourceBlock<TInput> Default => _default;

    public IReadOnlyDictionary<string, ISourceBlock<TInput>> RouteOutputs => _routeOutputs;

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
            ((IDataflowBlock)_routed).Fault(exception);
            ((IDataflowBlock)_matched).Fault(exception);
            ((IDataflowBlock)_default).Fault(exception);
            foreach (var routeOutput in _routeOutputBlocks.Values)
            {
                ((IDataflowBlock)routeOutput).Fault(exception);
            }
        }
    }

    private async Task RouteAsync(TInput input)
    {
        string? routeKey;
        try
        {
            _processingCancellationToken.ThrowIfCancellationRequested();
            routeKey = RoutingNodeSupport.EvaluateRouteKey(
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
                RoutingErrorCodes.SwitchExpressionFailed,
                $"flow.switch failed to evaluate input: {exception.Message}",
                exception,
                RoutingNodeSupport.CreateErrorContext(_options, _expressionEngine));
            TryEmitDiagnostic(
                RoutingDiagnosticNames.SwitchFailed,
                FlowDiagnosticLevel.Error,
                "flow.switch failed to evaluate input.",
                exception,
                RoutingNodeSupport.CreateAttributes(_options, _expressionEngine));
            return;
        }

        var matched = IsMatch(routeKey);
        var result = new FlowSwitchResult<TInput>
        {
            RouteKey = routeKey,
            Matched = matched,
            DefaultRoute = matched ? null : _options.DefaultRoute,
            Expression = _options.Expression!,
            ExpressionId = _options.ExpressionId,
            ExpressionName = _options.ExpressionName,
            InputType = _options.InputType,
            Value = input
        };

        await _result.SendAsync(result, _processingCancellationToken).ConfigureAwait(false);
        string? outputPort = null;
        if (matched && _options.EmitMatchedInput)
        {
            await _matched.SendAsync(input, _processingCancellationToken).ConfigureAwait(false);
        }

        if (matched
            && routeKey is not null
            && _routeOutputPorts.TryGetValue(routeKey, out var routeOutputPort)
            && _routeOutputBlocks.TryGetValue(routeOutputPort, out var routeOutput))
        {
            outputPort = routeOutputPort;
            await routeOutput.SendAsync(input, _processingCancellationToken).ConfigureAwait(false);
        }

        if (_options.EmitRouteEnvelope)
        {
            await _routed.SendAsync(
                new FlowRoute<TInput>
                {
                    RouteKey = routeKey,
                    Route = matched ? routeKey : _options.DefaultRoute,
                    Matched = matched,
                    DefaultRoute = matched ? null : _options.DefaultRoute,
                    OutputPort = outputPort,
                    ExpressionId = _options.ExpressionId,
                    ExpressionName = _options.ExpressionName,
                    InputType = _options.InputType,
                    Value = input
                },
                _processingCancellationToken).ConfigureAwait(false);
        }

        if (!matched && _options.EmitDefaultInput)
        {
            await _default.SendAsync(input, _processingCancellationToken).ConfigureAwait(false);
        }

        TryEmitDiagnostic(
            RoutingDiagnosticNames.SwitchRouted,
            message: matched
                ? "flow.switch matched route."
                : "flow.switch used default route.",
            attributes: RoutingNodeSupport.CreateAttributes(
                _options,
                _expressionEngine,
                routeKey,
                matched));
    }

    private bool IsMatch(string? routeKey)
    {
        if (routeKey is null)
        {
            return false;
        }

        return _routes.Count == 0 || _routes.Contains(routeKey);
    }

    private void CompleteOutputs(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_result).Fault(exception);
            ((IDataflowBlock)_routed).Fault(exception);
            ((IDataflowBlock)_matched).Fault(exception);
            ((IDataflowBlock)_default).Fault(exception);
            foreach (var routeOutput in _routeOutputBlocks.Values)
            {
                ((IDataflowBlock)routeOutput).Fault(exception);
            }

            return;
        }

        _result.Complete();
        _routed.Complete();
        _matched.Complete();
        _default.Complete();
        foreach (var routeOutput in _routeOutputBlocks.Values)
        {
            routeOutput.Complete();
        }
    }
}
