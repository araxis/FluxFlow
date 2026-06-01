using FluxFlow.Components.Control.Contracts;
using FluxFlow.Components.Control.Options;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Components.Control.Nodes;

internal static class ControlNodeFactory
{
    private static readonly MethodInfo CreateFilterMethod = GetMethod(nameof(CreateFilterTyped));
    private static readonly MethodInfo CreateWhenMethod = GetMethod(nameof(CreateWhenTyped));

    public static RuntimeNode CreateFilter(
        RuntimeNodeFactoryContext context,
        ControlComponentOptions componentOptions)
        => Create(context, componentOptions, ControlComponentTypes.Filter.Value, CreateFilterMethod);

    public static RuntimeNode CreateWhen(
        RuntimeNodeFactoryContext context,
        ControlComponentOptions componentOptions)
        => Create(context, componentOptions, ControlComponentTypes.When.Value, CreateWhenMethod);

    private static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        ControlComponentOptions componentOptions,
        string nodeType,
        MethodInfo createMethod)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = ControlOptionsReader.Read(context.Definition, nodeType);
        var inputType = componentOptions.ResolveType(options.InputType);
        var expressionEngine = componentOptions.ResolveExpressionEngine(options.Engine);
        var contextFactory = componentOptions.ResolveContextFactory(inputType);
        var nodeContext = new ControlNodeContext
        {
            Address = context.Address,
            Options = options,
            InputType = inputType
        };

        try
        {
            var method = createMethod.MakeGenericMethod(inputType);
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

    private static RuntimeNode CreateFilterTyped<TInput>(
        RuntimeNodeFactoryContext context,
        ControlExpressionOptions options,
        IFlowExpressionEngine expressionEngine,
        IControlContextFactory contextFactory,
        ControlNodeContext nodeContext)
    {
        var node = new FilterNode<TInput>(
            options,
            expressionEngine,
            contextFactory,
            nodeContext);

        return context.CreateNode(node)
            .Input(ControlComponentPorts.Input, node.Input)
            .Output(ControlComponentPorts.Output, node.Output)
            .Build();
    }

    private static RuntimeNode CreateWhenTyped<TInput>(
        RuntimeNodeFactoryContext context,
        ControlExpressionOptions options,
        IFlowExpressionEngine expressionEngine,
        IControlContextFactory contextFactory,
        ControlNodeContext nodeContext)
    {
        var node = new WhenNode<TInput>(
            options,
            expressionEngine,
            contextFactory,
            nodeContext);

        return context.CreateNode(node)
            .Input(ControlComponentPorts.Input, node.Input)
            .Output(ControlComponentPorts.WhenTrue, node.WhenTrue)
            .Output(ControlComponentPorts.WhenFalse, node.WhenFalse)
            .Build();
    }

    private static MethodInfo GetMethod(string name)
        => typeof(ControlNodeFactory).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find control factory method '{name}'.");
}
