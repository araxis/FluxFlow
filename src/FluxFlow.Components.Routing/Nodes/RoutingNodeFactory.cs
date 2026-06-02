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
    private static readonly MethodInfo CreateForkMethod = GetMethod(nameof(CreateForkTyped));
    private static readonly MethodInfo CreateMergeMethod = GetMethod(nameof(CreateMergeTyped));

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

    public static RuntimeNode CreateFork(
        RuntimeNodeFactoryContext context,
        RoutingComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = RoutingOptionsReader.ReadForkOptions(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);

        try
        {
            var method = CreateForkMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(null, [context, options])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public static RuntimeNode CreateMerge(
        RuntimeNodeFactoryContext context,
        RoutingComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = RoutingOptionsReader.ReadMergeOptions(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);

        try
        {
            var method = CreateMergeMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(null, [context, options])!;
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
            .Output(RoutingComponentPorts.Routed, node.Routed)
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

        var builder = context.CreateNode(node)
            .Output(RoutingComponentPorts.Matched, node.Matched)
            .Output(RoutingComponentPorts.Timeouts, node.Timeouts)
            .Output(RoutingComponentPorts.Errors, node.Errors);

        if (node.Input is not null)
        {
            builder.Input(RoutingComponentPorts.Input, node.Input);
        }

        if (node.Request is not null)
        {
            builder.Input(RoutingComponentPorts.Request, node.Request);
        }

        if (node.Response is not null)
        {
            builder.Input(RoutingComponentPorts.Response, node.Response);
        }

        return builder.Build();
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

    private static RuntimeNode CreateForkTyped<TInput>(
        RuntimeNodeFactoryContext context,
        ForkRoutingOptions options)
    {
        var node = new FlowForkNode<TInput>(options);
        var builder = context.CreateNode(node)
            .Input(RoutingComponentPorts.Input, node.Input)
            .Output(RoutingComponentPorts.Errors, node.Errors);

        foreach (var (portName, output) in node.Outputs)
        {
            builder.Output(portName, output);
        }

        return builder.Build();
    }

    private static RuntimeNode CreateMergeTyped<TInput>(
        RuntimeNodeFactoryContext context,
        MergeRoutingOptions options)
    {
        var node = new FlowMergeNode<TInput>(options);
        var builder = context.CreateNode(node)
            .Output(RoutingComponentPorts.Output, node.Output)
            .Output(RoutingComponentPorts.Errors, node.Errors);

        foreach (var (portName, input) in node.Inputs)
        {
            builder.Input(portName, input);
        }

        return builder.Build();
    }

    private static MethodInfo GetMethod(string name)
        => typeof(RoutingNodeFactory).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find routing factory method '{name}'.");
}
