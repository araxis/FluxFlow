using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Observability.Composition;

public static class ObservabilityCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterCounter<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = ObservabilityCompositionNodeTypes.Counter)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateCounterNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    ObservabilityCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowCounterSnapshot>(
                    ObservabilityCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterLogger<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = ObservabilityCompositionNodeTypes.Logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateLoggerNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    ObservabilityCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowLogEntry>(
                    ObservabilityCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterMetrics<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = ObservabilityCompositionNodeTypes.Metrics)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateMetricsNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    ObservabilityCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowMetricSnapshot>(
                    ObservabilityCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateCounterNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FlowCounterOptions>();
        var expressionEngine = RequiresExpressionEngine(options)
            ? context.GetRequiredResource<IFlowExpressionEngine>(
                ObservabilityCompositionResourceNames.Engine)
            : null;
        var contextFactory = context.GetResource<IFlowMapContextFactory<TInput>>(
            ObservabilityCompositionResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityCompositionResourceNames.Clock);
        var node = new FlowCounterNode<TInput>(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    ObservabilityCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowCounterSnapshot>(
                    ObservabilityCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateLoggerNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FlowLoggerOptions>();
        var attributeSelectors = ResolveAttributeSelectors<TInput>(
            context,
            options);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityCompositionResourceNames.Clock);
        var node = new FlowLoggerNode<TInput>(
            options,
            attributeSelectors,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    ObservabilityCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowLogEntry>(
                    ObservabilityCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateMetricsNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FlowMetricsOptions>();
        var sizeSelector = context.GetResource<IObservabilityValueSelector<TInput>>(
            ObservabilityCompositionResourceNames.SizeSelector);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityCompositionResourceNames.Clock);
        var node = new FlowMetricsNode<TInput>(
            options,
            sizeSelector,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    ObservabilityCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowMetricSnapshot>(
                    ObservabilityCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static IReadOnlyDictionary<string, IObservabilityValueSelector<TInput>>
        ResolveAttributeSelectors<TInput>(
            CompositionNodeFactoryContext context,
            FlowLoggerOptions options)
    {
        var selectors = new Dictionary<string, IObservabilityValueSelector<TInput>>(
            StringComparer.Ordinal);
        foreach (var configuredName in options.AttributeSelectors ?? [])
        {
            var name = NormalizeAttributeSelectorName(configuredName);
            var resourceName = ObservabilityCompositionResourceNames.AttributeSelector(
                name);
            var selector = context.GetRequiredResource<IObservabilityValueSelector<TInput>>(
                resourceName);
            if (!selectors.TryAdd(name, selector))
            {
                throw new InvalidOperationException(
                    $"flow.logger attribute selector '{name}' is configured more than once.");
            }
        }

        return selectors;
    }

    private static string NormalizeAttributeSelectorName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "flow.logger attribute selector names must be non-empty.");
        }

        return name.Trim();
    }

    private static bool RequiresExpressionEngine(FlowCounterOptions options)
        => !string.IsNullOrWhiteSpace(options.Predicate)
            || !string.IsNullOrWhiteSpace(options.Expression);
}
