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
            Options = options,
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

        return context.CreateNode(node)
            .Input(RoutingComponentPorts.Input, node.Input)
            .Output(RoutingComponentPorts.Result, node.Result)
            .Output(RoutingComponentPorts.Matched, node.Matched)
            .Output(RoutingComponentPorts.Default, node.Default)
            .Output(RoutingComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static MethodInfo GetMethod(string name)
        => typeof(RoutingNodeFactory).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find routing factory method '{name}'.");
}
