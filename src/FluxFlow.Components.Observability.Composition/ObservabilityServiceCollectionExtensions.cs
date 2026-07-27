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
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                CounterDescriptor,
                LoggerDescriptor,
                MetricsDescriptor
            ],
            ObservabilityComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor CounterDescriptor { get; } = new(
        ObservabilityComponentDefinition.Types.Counter,
        CreateCounterNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(ObservabilityComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowCounterSnapshot>(ObservabilityComponentDefinition.Ports.Output)
        ],
        options: ObservabilityComponentDefinition.CreateOptions(ObservabilityComponentDefinition.Types.Counter),
        resources: ObservabilityComponentDefinition.CreateResources(ObservabilityComponentDefinition.Types.Counter));

    internal static ComponentDescriptor LoggerDescriptor { get; } = new(
        ObservabilityComponentDefinition.Types.Logger,
        CreateLoggerNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(ObservabilityComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowLogEntry<JsonElement>>(ObservabilityComponentDefinition.Ports.Output)
        ],
        options: ObservabilityComponentDefinition.CreateOptions(ObservabilityComponentDefinition.Types.Logger),
        resources: ObservabilityComponentDefinition.CreateResources(ObservabilityComponentDefinition.Types.Logger));

    internal static ComponentDescriptor MetricsDescriptor { get; } = new(
        ObservabilityComponentDefinition.Types.Metrics,
        CreateMetricsNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(ObservabilityComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FlowMetricSnapshot>(ObservabilityComponentDefinition.Ports.Output)
        ],
        options: ObservabilityComponentDefinition.CreateOptions(ObservabilityComponentDefinition.Types.Metrics),
        resources: ObservabilityComponentDefinition.CreateResources(ObservabilityComponentDefinition.Types.Metrics));

    public static IServiceCollection AddObservabilityComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
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
                ObservabilityComponentDefinition.Resources.Engine)
            : null;
        var contextFactory = context.GetResource<IFlowMapContextFactory<JsonElement>>(
            ObservabilityComponentDefinition.Resources.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityComponentDefinition.Resources.Clock);
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
                    ObservabilityComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowCounterSnapshot>(
                    ObservabilityComponentDefinition.Ports.Output,
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
            ObservabilityComponentDefinition.Resources.Clock);
        var node = new FlowLoggerNode(options, attributeSelectors, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    ObservabilityComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowLogEntry<JsonElement>>(
                    ObservabilityComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateMetricsNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FlowMetricsOptions>();
        var sizeSelector = context.GetResource<IObservabilityValueSelector<JsonElement>>(
            ObservabilityComponentDefinition.Resources.SizeSelector);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityComponentDefinition.Resources.Clock);
        var node = new FlowMetricsNode(options, sizeSelector, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    ObservabilityComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FlowMetricSnapshot>(
                    ObservabilityComponentDefinition.Ports.Output,
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
            var resourceName = ObservabilityComponentDefinition.Resources.AttributeSelector(name);
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
