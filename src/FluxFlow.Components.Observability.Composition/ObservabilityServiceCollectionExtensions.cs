using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Observability.Composition;

public static class ObservabilityServiceCollectionExtensions
{
    internal static ComponentDescriptor CounterDescriptor { get; } = new(
        ObservabilityComponentTypes.Counter,
        CreateCounterNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(ObservabilityComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowCounterSnapshot>(ObservabilityComponentPortNames.Output)
        ]);

    internal static ComponentDescriptor LoggerDescriptor { get; } = new(
        ObservabilityComponentTypes.Logger,
        CreateLoggerNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(ObservabilityComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowLogEntry<JsonElement>>(ObservabilityComponentPortNames.Output)
        ]);

    internal static ComponentDescriptor MetricsDescriptor { get; } = new(
        ObservabilityComponentTypes.Metrics,
        CreateMetricsNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(ObservabilityComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowMetricSnapshot>(ObservabilityComponentPortNames.Output)
        ]);

    public static IServiceCollection AddObservabilityComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(CounterDescriptor);
        services.AddFluxFlowComponent(LoggerDescriptor);
        services.AddFluxFlowComponent(MetricsDescriptor);
        services.AddComponentDesignMetadataProvider<ObservabilityComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateCounterNode(
        ComponentActivationContext context)
    {
        if (context.Component.Properties.Keys.Any(static name =>
                string.Equals(name, "expression", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Counter option 'expression' is no longer supported. Use 'predicate'.");
        }

        var options = context.BindConfiguration<FlowCounterOptions>();
        var expressionEngine = RequiresExpressionEngine(options)
            ? context.GetRequiredResource<IFlowExpressionEngine>(
                ObservabilityComponentResourceNames.Engine)
            : null;
        var contextFactory = context.GetResource<IFlowMapContextFactory<JsonElement>>(
            ObservabilityComponentResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityComponentResourceNames.Clock);
        var node = new FlowCounterNode(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    ObservabilityComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowCounterSnapshot>(
                    ObservabilityComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateLoggerNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FlowLoggerOptions>();
        var attributeSelectors = ResolveAttributeSelectors(context, options);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityComponentResourceNames.Clock);
        var node = new FlowLoggerNode(options, attributeSelectors, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    ObservabilityComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowLogEntry<JsonElement>>(
                    ObservabilityComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateMetricsNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FlowMetricsOptions>();
        var sizeSelector = context.GetResource<IObservabilityValueSelector<JsonElement>>(
            ObservabilityComponentResourceNames.SizeSelector);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityComponentResourceNames.Clock);
        var node = new FlowMetricsNode(options, sizeSelector, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    ObservabilityComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowMetricSnapshot>(
                    ObservabilityComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static IReadOnlyDictionary<string, IObservabilityValueSelector<JsonElement>>
        ResolveAttributeSelectors(
            ComponentActivationContext context,
            FlowLoggerOptions options)
    {
        var selectors = new Dictionary<string, IObservabilityValueSelector<JsonElement>>(
            StringComparer.Ordinal);
        foreach (var configuredName in options.AttributeSelectors ?? [])
        {
            var name = NormalizeAttributeSelectorName(configuredName);
            var resourceName = ObservabilityComponentResourceNames.AttributeSelector(name);
            var selector = context.GetRequiredResource<IObservabilityValueSelector<JsonElement>>(
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
        => !string.IsNullOrWhiteSpace(options.Predicate);
}
