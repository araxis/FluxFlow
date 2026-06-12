using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Components.Assertions.Timing;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Components.Assertions.Nodes;

internal static class AssertionNodeFactory
{
    private static readonly MethodInfo CreateAssertMethod = GetMethod(nameof(CreateAssertTyped));

    public static RuntimeNode CreateAssert(
        RuntimeNodeFactoryContext context,
        AssertionsComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = AssertionOptionsReader.Read(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);
        var expressionEngine = componentOptions.ResolveExpressionEngine(options.Engine);
        var contextFactory = componentOptions.ResolveContextFactory(inputType);
        var nodeContext = new AssertionNodeContext
        {
            Address = context.Address,
            Options = options,
            InputType = inputType
        };

        try
        {
            var method = CreateAssertMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(
                null,
                [context, options, expressionEngine, contextFactory, nodeContext, componentOptions.Clock])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static RuntimeNode CreateAssertTyped<TInput>(
        RuntimeNodeFactoryContext context,
        AssertionOptions options,
        IFlowExpressionEngine expressionEngine,
        IAssertionContextFactory contextFactory,
        AssertionNodeContext nodeContext,
        IAssertionClock clock)
    {
        var node = new FlowAssertionComponent<TInput>(
            options,
            expressionEngine,
            contextFactory,
            nodeContext,
            clock);

        return context.CreateNode(node)
            .Input(AssertionsComponentPorts.Input, node.Input)
            .Output(AssertionsComponentPorts.Result, node.Result)
            .Output(AssertionsComponentPorts.Passed, node.Passed)
            .Output(AssertionsComponentPorts.Failed, node.Failed)
            .Output(AssertionsComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static MethodInfo GetMethod(string name)
        => typeof(AssertionNodeFactory).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find assertion factory method '{name}'.");
}
