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
    private static readonly MethodInfo CreateWindowMethod = GetMethod(nameof(CreateWindowTyped));
    private static readonly MethodInfo CreateJoinMethod = GetMethod(nameof(CreateJoinTyped));

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

    public static RuntimeNode CreateWindow(
        RuntimeNodeFactoryContext context,
        RoutingComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = RoutingOptionsReader.ReadWindowOptions(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);

        try
        {
            var method = CreateWindowMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(null, [context, options])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public static RuntimeNode CreateJoin(
        RuntimeNodeFactoryContext context,
        RoutingComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = RoutingOptionsReader.ReadJoinOptions(context.Definition);
        var leftType = componentOptions.ResolveType(options.LeftInputType);
        var rightType = componentOptions.ResolveType(options.RightInputType);
        var expressionEngine = componentOptions.ResolveExpressionEngine(options.Engine);
        var leftContextFactory = componentOptions.ResolveContextFactory(leftType);
        var rightContextFactory = componentOptions.ResolveContextFactory(rightType);
        var leftNodeContext = new RoutingNodeContext
        {
            Address = context.Address,
            NodeType = RoutingComponentTypes.Join,
            InputType = leftType
        };
        var rightNodeContext = new RoutingNodeContext
        {
            Address = context.Address,
            NodeType = RoutingComponentTypes.Join,
            InputType = rightType
        };

        try
        {
            var method = CreateJoinMethod.MakeGenericMethod(leftType, rightType);
            return (RuntimeNode)method.Invoke(
                null,
                [
                    context,
                    options,
                    expressionEngine,
                    leftContextFactory,
                    rightContextFactory,
                    leftNodeContext,
                    rightNodeContext
                ])!;
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

    private static RuntimeNode CreateWindowTyped<TInput>(
        RuntimeNodeFactoryContext context,
        WindowRoutingOptions options)
    {
        var node = new FlowWindowNode<TInput>(options);

        return context.CreateNode(node)
            .Input(RoutingComponentPorts.Input, node.Input)
            .Output(RoutingComponentPorts.Output, node.Output)
            .Output(RoutingComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static RuntimeNode CreateJoinTyped<TLeft, TRight>(
        RuntimeNodeFactoryContext context,
        JoinRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        IRoutingContextFactory leftContextFactory,
        IRoutingContextFactory rightContextFactory,
        RoutingNodeContext leftNodeContext,
        RoutingNodeContext rightNodeContext)
    {
        var node = new FlowJoinNode<TLeft, TRight>(
            options,
            expressionEngine,
            leftContextFactory,
            rightContextFactory,
            leftNodeContext,
            rightNodeContext);

        return context.CreateNode(node)
            .Input(RoutingComponentPorts.Left, node.Left)
            .Input(RoutingComponentPorts.Right, node.Right)
            .Output(RoutingComponentPorts.Output, node.Output)
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
