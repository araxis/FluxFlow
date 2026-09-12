using System.Text.Json;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Observability.Composition;

public static class ObservabilityServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddObservability(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(ObservabilityComponents.Counter)
            .AddComponent(ObservabilityComponents.Logger)
            .AddComponent(ObservabilityComponents.Metrics);
    }

    internal static void ConfigureCounter(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<string>(ObservabilityComponentDefinition.Options.Name);
        component.AddOption<string>(ObservabilityComponentDefinition.Options.Predicate);
        component.AddOption<string>(ObservabilityComponentDefinition.Options.ExpressionId);
        component.AddOption<string>(ObservabilityComponentDefinition.Options.ExpressionName);
        component.AddOption<int>(ObservabilityComponentDefinition.Options.BoundedCapacity);
        component.AddResource<IFlowExpressionEngine>(ObservabilityComponentDefinition.Resources.Engine);
        component.AddResource<IFlowMapContextFactory<JsonElement>>(ObservabilityComponentDefinition.Resources.ContextFactory);
        component.AddResource<TimeProvider>(ObservabilityComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateCounterNode)
            .HasStandardPorts(FlowValueMaterializers.JsonElement.InputShape);
    }

    internal static void ConfigureLogger(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<string>(ObservabilityComponentDefinition.Options.Level);
        component.AddOption<string>(ObservabilityComponentDefinition.Options.Category);
        component.AddOption<string>(ObservabilityComponentDefinition.Options.MessageTemplate);
        component.AddOption<string[]>(ObservabilityComponentDefinition.Options.AttributeSelectors);
        component.AddOption<int>(ObservabilityComponentDefinition.Options.BoundedCapacity);
        component.AddResource<TimeProvider>(ObservabilityComponentDefinition.Resources.Clock);
        component.AddResource<IObservabilityValueSelector<JsonElement>>(ObservabilityComponentDefinition.Resources.AttributeSelectorPrefix + "{name}");
        component
            .UseFactory(CreateLoggerNode)
            .HasStandardPorts(FlowValueMaterializers.JsonElement.InputShape);
    }

    internal static void ConfigureMetrics(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<string>(ObservabilityComponentDefinition.Options.Name);
        component.AddOption<int>(ObservabilityComponentDefinition.Options.BoundedCapacity);
        component.AddResource<IObservabilityValueSelector<JsonElement>>(ObservabilityComponentDefinition.Resources.SizeSelector);
        component.AddResource<TimeProvider>(ObservabilityComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateMetricsNode)
            .HasStandardPorts(FlowValueMaterializers.JsonElement.InputShape);
    }

    private static FlowCounterNode CreateCounterNode(
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
        return new FlowCounterNode(
            options,
            expressionEngine,
            contextFactory,
            clock);
    }

    private static FlowLoggerNode CreateLoggerNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FlowLoggerOptions>();
        var attributeSelectors = ResolveAttributeSelectors(context, options);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityComponentDefinition.Resources.Clock);
        return new FlowLoggerNode(options, attributeSelectors, clock);
    }

    private static FlowMetricsNode CreateMetricsNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FlowMetricsOptions>();
        var sizeSelector = context.GetResource<IObservabilityValueSelector<JsonElement>>(
            ObservabilityComponentDefinition.Resources.SizeSelector);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityComponentDefinition.Resources.Clock);
        return new FlowMetricsNode(options, sizeSelector, clock);
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
