using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Components.Mapping.Nodes;

internal static class FlowMapperNodeFactory
{
    private static readonly MethodInfo CreateTypedMethod =
        typeof(FlowMapperNodeFactory).GetMethod(
            nameof(CreateTyped),
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find typed mapper factory method.");

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        MappingComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = MappingOptionsReader.ReadMapperOptions(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);
        var outputType = componentOptions.ResolveType(options.EffectiveOutputType);
        var expressionEngine = componentOptions.ResolveExpressionEngine(options.Engine);
        var contextFactory = componentOptions.ResolveContextFactory(inputType);
        var nodeContext = new MappingNodeContext
        {
            Address = context.Address,
            Options = options,
            InputType = inputType,
            OutputType = outputType
        };

        try
        {
            var method = CreateTypedMethod.MakeGenericMethod(inputType, outputType);
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

    private static RuntimeNode CreateTyped<TInput, TOutput>(
        RuntimeNodeFactoryContext context,
        MapperOptions options,
        IFlowExpressionEngine expressionEngine,
        IMappingContextFactory contextFactory,
        MappingNodeContext nodeContext)
    {
        var node = new FlowMapperNode<TInput, TOutput>(
            options,
            expressionEngine,
            contextFactory,
            nodeContext);

        return context.CreateNode(node)
            .Input(MappingComponentPorts.Input, node.Input)
            .Output(MappingComponentPorts.Output, node.Output)
            .Output(MappingComponentPorts.Errors, node.Errors)
            .Build();
    }
}
