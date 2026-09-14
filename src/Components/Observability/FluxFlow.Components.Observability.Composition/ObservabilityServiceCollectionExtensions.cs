using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
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
    public static IServiceCollection AddObservability(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(ObservabilityComponentDefinition.Types.Counter, ObservabilityServiceCollectionExtensions.CreateCounterNode)
            .AddComponent(ObservabilityComponentDefinition.Types.Logger, ObservabilityServiceCollectionExtensions.CreateLoggerNode)
            .AddComponent(ObservabilityComponentDefinition.Types.Metrics, ObservabilityServiceCollectionExtensions.CreateMetricsNode);
    }

    internal static ValueTask<IFlowNode> CreateCounterNode(
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
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateLoggerNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FlowLoggerOptions>();
        var attributeSelectors = ResolveAttributeSelectors(context, options);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityComponentDefinition.Resources.Clock);
        var node = new FlowLoggerNode(options, attributeSelectors, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateMetricsNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FlowMetricsOptions>();
        var sizeSelector = context.GetResource<IObservabilityValueSelector<JsonElement>>(
            ObservabilityComponentDefinition.Resources.SizeSelector);
        var clock = context.GetResource<TimeProvider>(
            ObservabilityComponentDefinition.Resources.Clock);
        var node = new FlowMetricsNode(options, sizeSelector, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
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
