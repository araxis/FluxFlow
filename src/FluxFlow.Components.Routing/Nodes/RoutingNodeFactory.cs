using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Components.Routing.Nodes;

internal static class RoutingNodeFactory
{
    private static readonly MethodInfo CreateSwitchMethod = GetMethod(nameof(CreateSwitchTyped));
    private static readonly MethodInfo CreateCorrelationMethod = GetMethod(nameof(CreateCorrelationTyped));

    public static RuntimeNode CreateSwitch(
        RuntimeNodeFactoryContext context,
        RoutingComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = RoutingOptionsReader.ReadSwitchOptions(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);
        var expressionEngine = componentOptions.ResolveExpressionEngine(options.Engine);
        var contextFactory = componentOptions.ResolveContextFactory(inputType);
        var nodeContext = new RoutingNodeContext
        {
            Address = context.Address,
            NodeType = RoutingComponentTypes.Switch,
            InputType = inputType
        };

        try
        {
            var method = CreateSwitchMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(
                null,
                [context, options, expressionEngine, contextFactory, nodeContext])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public static RuntimeNode CreateCorrelation(
        RuntimeNodeFactoryContext context,
        RoutingComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = RoutingOptionsReader.ReadCorrelationOptions(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);
        var expressionEngine = componentOptions.ResolveExpressionEngine(options.Engine);
        var contextFactory = componentOptions.ResolveContextFactory(inputType);
        var nodeContext = new RoutingNodeContext
        {
            Address = context.Address,
            NodeType = RoutingComponentTypes.Correlation,
            InputType = inputType
        };

        try
        {
            var method = CreateCorrelationMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(
                null,
                [context, options, expressionEngine, contextFactory, nodeContext])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static RuntimeNode CreateSwitchTyped<TInput>(
        RuntimeNodeFactoryContext context,
        SwitchRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        IRoutingContextFactory contextFactory,
        RoutingNodeContext nodeContext)
    {
        var node = new FlowSwitchNode<TInput>(
            options,
            expressionEngine,
            contextFactory,
            nodeContext);

        var builder = context.CreateNode(node)
            .Input(RoutingComponentPorts.Input, node.Input)
            .Output(RoutingComponentPorts.Result, node.Result)
            .Output(RoutingComponentPorts.Matched, node.Matched)
            .Output(RoutingComponentPorts.Default, node.Default)
            .Output(RoutingComponentPorts.Errors, node.Errors);

        foreach (var (portName, output) in node.RouteOutputs)
        {
            builder.Output(portName, output);
        }

        return builder.Build();
    }

    private static RuntimeNode CreateCorrelationTyped<TInput>(
        RuntimeNodeFactoryContext context,
        CorrelationRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        IRoutingContextFactory contextFactory,
        RoutingNodeContext nodeContext)
    {
        var node = new FlowCorrelationNode<TInput>(
            options,
            expressionEngine,
            contextFactory,
            nodeContext);

        return context.CreateNode(node)
            .Input(RoutingComponentPorts.Input, node.Input)
            .Output(RoutingComponentPorts.Matched, node.Matched)
            .Output(RoutingComponentPorts.Timeouts, node.Timeouts)
            .Output(RoutingComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static MethodInfo GetMethod(string name)
        => typeof(RoutingNodeFactory).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find routing factory method '{name}'.");
}
