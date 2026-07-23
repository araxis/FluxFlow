using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Observability.Composition;

public static class ObservabilityCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterCounter(
        this CompositionNodeRegistry registry,
        string nodeType = ObservabilityCompositionNodeTypes.Counter)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            ObservabilityCompositionNodeTypes.CounterDescriptor,
            CreateCounterNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    ObservabilityCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowCounterSnapshot>>(
                    ObservabilityCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    public static CompositionNodeRegistry RegisterLogger(
        this CompositionNodeRegistry registry,
        string nodeType = ObservabilityCompositionNodeTypes.Logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            ObservabilityCompositionNodeTypes.LoggerDescriptor,
            CreateLoggerNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    ObservabilityCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowLogEntry>>(
                    ObservabilityCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    public static CompositionNodeRegistry RegisterMetrics(
        this CompositionNodeRegistry registry,
        string nodeType = ObservabilityCompositionNodeTypes.Metrics)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            ObservabilityCompositionNodeTypes.MetricsDescriptor,
            CreateMetricsNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    ObservabilityCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowMetricSnapshot>>(
                    ObservabilityCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    private static ValueTask<ComposedNode> CreateCounterNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FlowCounterOptions>();
        var expressionEngine = RequiresExpressionEngine(options)
            ? context.GetRequiredResource<IFlowExpressionEngine>(
                ObservabilityCompositionResourceNames.Engine)
            : null;
        var contextFactory = context.GetResource<IFlowMapContextFactory<FlowValue>>(
            ObservabilityCompositionResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityCompositionResourceNames.Clock);
        var node = new FlowCounterNode(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValue>(
                    ObservabilityCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowCounterSnapshot>>(
                    ObservabilityCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateLoggerNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FlowLoggerOptions>();
        var attributeSelectors = ResolveAttributeSelectors(context, options);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityCompositionResourceNames.Clock);
        var node = new FlowLoggerNode(options, attributeSelectors, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValue>(
                    ObservabilityCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowLogEntry>>(
                    ObservabilityCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateMetricsNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FlowMetricsOptions>();
        var sizeSelector = context.GetResource<IObservabilityValueSelector>(
            ObservabilityCompositionResourceNames.SizeSelector);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityCompositionResourceNames.Clock);
        var node = new FlowMetricsNode(options, sizeSelector, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValue>(
                    ObservabilityCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowMetricSnapshot>>(
                    ObservabilityCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static IReadOnlyDictionary<string, IObservabilityValueSelector>
        ResolveAttributeSelectors(
            CompositionNodeFactoryContext context,
            FlowLoggerOptions options)
    {
        var selectors = new Dictionary<string, IObservabilityValueSelector>(
            StringComparer.Ordinal);
        foreach (var configuredName in options.AttributeSelectors ?? [])
        {
            var name = NormalizeAttributeSelectorName(configuredName);
            var resourceName = ObservabilityCompositionResourceNames.AttributeSelector(name);
            var selector = context.GetRequiredResource<IObservabilityValueSelector>(
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
