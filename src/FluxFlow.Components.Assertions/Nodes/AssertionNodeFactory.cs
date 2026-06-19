using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Mapping;
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
        TimeProvider clock)
    {
        // Compile the predicate expression once at build time; IsMatch only
        // evaluates the compiled form per message.
        var predicate = new ExpressionFlowPredicate<TInput>(
            options.Expression!,
            expressionEngine,
            new AssertionContextAdapter<TInput>(contextFactory, nodeContext));
        var metadata = CreateMetadata(options, expressionEngine.Name);
        var node = new FlowAssertionComponent<TInput>(predicate, metadata, clock);

        return context.CreateNode(node)
            .Input(AssertionsComponentPorts.Input, node.Input)
            .Output(AssertionsComponentPorts.Result, node.Result)
            .Output(AssertionsComponentPorts.Passed, node.Passed)
            .Output(AssertionsComponentPorts.Failed, node.Failed)
            .Output(AssertionsComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static AssertionResultMetadata CreateMetadata(AssertionOptions options, string engineName)
        => new()
        {
            EffectiveDescription = options.EffectiveDescription,
            Expression = options.Expression!,
            ExpressionId = options.ExpressionId,
            ExpressionName = options.ExpressionName,
            EngineName = engineName,
            InputType = options.InputType,
            EffectiveFailureMessage = options.EffectiveFailureMessage,
            EmitPassedInput = options.EmitPassedInput,
            EmitFailedInput = options.EmitFailedInput,
            BoundedCapacity = options.BoundedCapacity
        };

    private static MethodInfo GetMethod(string name)
        => typeof(AssertionNodeFactory).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find assertion factory method '{name}'.");

    private sealed class AssertionContextAdapter<TInput>(
        IAssertionContextFactory inner,
        AssertionNodeContext nodeContext)
        : IFlowMapContextFactory<TInput>
    {
        public FlowMapContext Create(TInput input)
            => inner.Create(input, nodeContext);
    }
}
