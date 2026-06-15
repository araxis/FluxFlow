using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Components.Observability.Timing;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Components.Observability.Nodes;

internal static class ObservabilityNodeFactory
{
    private static readonly MethodInfo CreateCounterMethod = GetMethod(nameof(CreateCounterTyped));
    private static readonly MethodInfo CreateLoggerMethod = GetMethod(nameof(CreateLoggerTyped));
    private static readonly MethodInfo CreateMetricsMethod = GetMethod(nameof(CreateMetricsTyped));

    public static RuntimeNode CreateCounter(
        RuntimeNodeFactoryContext context,
        ObservabilityComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = ObservabilityOptionsReader.ReadCounterOptions(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);
        var expressionEngine = string.IsNullOrWhiteSpace(options.EffectivePredicate)
            ? null
            : componentOptions.ResolveExpressionEngine(options.Engine);
        var contextFactory = componentOptions.ResolveContextFactory(inputType);
        var nodeContext = CreateNodeContext(
            context,
            ObservabilityComponentTypes.Counter.Value,
            inputType,
            options.EffectiveName);

        return Create(
            CreateCounterMethod,
            inputType,
            context,
            options,
            expressionEngine,
            contextFactory,
            nodeContext,
            componentOptions.Clock);
    }

    public static RuntimeNode CreateLogger(
        RuntimeNodeFactoryContext context,
        ObservabilityComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = ObservabilityOptionsReader.ReadLoggerOptions(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);
        var attributeSelectors = (options.AttributeSelectors ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                name => name,
                name => componentOptions.ResolveValueSelector(inputType, name),
                StringComparer.OrdinalIgnoreCase);
        var nodeContext = CreateNodeContext(
            context,
            ObservabilityComponentTypes.Logger.Value,
            inputType,
            options.EffectiveCategory);

        return Create(
            CreateLoggerMethod,
            inputType,
            context,
            options,
            attributeSelectors,
            nodeContext,
            componentOptions.Clock);
    }

    public static RuntimeNode CreateMetrics(
        RuntimeNodeFactoryContext context,
        ObservabilityComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = ObservabilityOptionsReader.ReadMetricsOptions(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);
        var sizeSelector = componentOptions.ResolveOptionalValueSelector(
            inputType,
            options.SizeSelector);
        var nodeContext = CreateNodeContext(
            context,
            ObservabilityComponentTypes.Metrics.Value,
            inputType,
            options.EffectiveName);

        return Create(
            CreateMetricsMethod,
            inputType,
            context,
            options,
            sizeSelector,
            nodeContext,
            componentOptions.Clock);
    }

    private static RuntimeNode Create(
        MethodInfo createMethod,
        Type inputType,
        params object?[] parameters)
    {
        try
        {
            var method = createMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(null, parameters)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static RuntimeNode CreateCounterTyped<TInput>(
        RuntimeNodeFactoryContext context,
        FlowCounterOptions options,
        IFlowExpressionEngine? expressionEngine,
        IObservabilityContextFactory contextFactory,
        ObservabilityNodeContext nodeContext,
        IObservabilityClock clock)
    {
        // Compile the predicate expression once at build time (when present);
        // when there is no predicate the node accepts every input and no engine
        // is required.
        var acceptPredicate = expressionEngine is null
            ? null
            : new ExpressionFlowPredicate<TInput>(
                options.EffectivePredicate!,
                expressionEngine,
                new ObservabilityContextAdapter<TInput>(contextFactory, nodeContext));
        var node = new FlowCounterNode<TInput>(
            options,
            acceptPredicate,
            expressionEngine?.Name,
            clock);

        return context.CreateNode(node)
            .Input(ObservabilityComponentPorts.Input, node.Input)
            .Output(ObservabilityComponentPorts.Snapshots, node.Snapshots)
            .Output(ObservabilityComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static RuntimeNode CreateLoggerTyped<TInput>(
        RuntimeNodeFactoryContext context,
        FlowLoggerOptions options,
        IReadOnlyDictionary<string, ObservabilityComponentOptions.IValueSelector> attributeSelectors,
        ObservabilityNodeContext nodeContext,
        IObservabilityClock clock)
    {
        var node = new FlowLoggerNode<TInput>(
            options,
            attributeSelectors,
            nodeContext,
            clock);

        return context.CreateNode(node)
            .Input(ObservabilityComponentPorts.Input, node.Input)
            .Output(ObservabilityComponentPorts.Entries, node.Entries)
            .Output(ObservabilityComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static RuntimeNode CreateMetricsTyped<TInput>(
        RuntimeNodeFactoryContext context,
        FlowMetricsOptions options,
        ObservabilityComponentOptions.IValueSelector? sizeSelector,
        ObservabilityNodeContext nodeContext,
        IObservabilityClock clock)
    {
        var node = new FlowMetricsNode<TInput>(
            options,
            sizeSelector,
            nodeContext,
            clock);

        return context.CreateNode(node)
            .Input(ObservabilityComponentPorts.Input, node.Input)
            .Output(ObservabilityComponentPorts.Snapshots, node.Snapshots)
            .Output(ObservabilityComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static ObservabilityNodeContext CreateNodeContext(
        RuntimeNodeFactoryContext context,
        string nodeType,
        Type inputType,
        string name)
        => new()
        {
            Address = context.Address,
            NodeType = nodeType,
            InputType = inputType,
            Name = name
        };

    private static MethodInfo GetMethod(string name)
        => typeof(ObservabilityNodeFactory).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find observability factory method '{name}'.");

    private sealed class ObservabilityContextAdapter<TInput>(
        IObservabilityContextFactory contextFactory,
        ObservabilityNodeContext nodeContext)
        : IFlowMapContextFactory<TInput>
    {
        public FlowMapContext Create(TInput input)
            => contextFactory.Create(input, nodeContext);
    }
}
