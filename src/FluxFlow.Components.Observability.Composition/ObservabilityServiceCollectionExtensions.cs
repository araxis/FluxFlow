using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Observability.Composition;

public static class ObservabilityServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddObservability(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(ObservabilityComponentDefinition.Types.Counter, ConfigureCounter)
            .AddComponent(ObservabilityComponentDefinition.Types.Logger, ConfigureLogger)
            .AddComponent(ObservabilityComponentDefinition.Types.Metrics, ConfigureMetrics);
    }

    private static void ConfigureCounter(ComponentRegistrationBuilder component)
    {
        var defaults = new FlowCounterOptions();
        ConfigureCommon<JsonElement, FlowCounterSnapshot>(component, CreateCounterNode, "Counter", "Counts accepted input messages and emits counter snapshots.", "hash", "count", "JSON value to count.", "Current counter snapshot or workflow error.");
        component.AddOption<string>(ObservabilityComponentDefinition.Options.Name, OptionValueKind.Text, "Name", "Optional counter name included in snapshots and diagnostics.", section: "Counter", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(ObservabilityComponentDefinition.Options.Predicate, OptionValueKind.Expression, "Predicate", "Optional boolean expression used to accept or reject inputs.", section: "Filtering", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Expression, syntax: OptionDesignMetadataAttributeValues.Expression, relatedResource: ObservabilityComponentDefinition.Resources.Engine);
        component.AddOption<string>(ObservabilityComponentDefinition.Options.ExpressionId, OptionValueKind.Text, "Expression ID", "Optional diagnostic identifier emitted with counter diagnostics.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(ObservabilityComponentDefinition.Options.ExpressionName, OptionValueKind.Text, "Expression Name", "Optional diagnostic name emitted with counter diagnostics.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        AddCapacity(component, defaults.BoundedCapacity);
        component.AddResource<IFlowExpressionEngine>(ObservabilityComponentDefinition.Resources.Engine, "Expression Engine", 0, "Conditionally required keyed expression engine when predicate is configured.", designValueType: nameof(IFlowExpressionEngine), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.ExpressionEngine, keyPattern: "expression-engine:{name}", requiredWhenAnyOption: ObservabilityComponentDefinition.Options.Predicate);
        component.AddResource<IFlowMapContextFactory<JsonElement>>(ObservabilityComponentDefinition.Resources.ContextFactory, "Context Factory", 1, "Optional keyed mapping context factory used when evaluating counter predicates.", designValueType: "IFlowMapContextFactory<JsonElement>", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.ContextFactory, keyPattern: "context-factory:{name}");
        component.AddResource<TimeProvider>(ObservabilityComponentDefinition.Resources.Clock, "Clock", 2, "Optional keyed clock for deterministic observability timestamps and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
    }

    private static void ConfigureLogger(ComponentRegistrationBuilder component)
    {
        var defaults = new FlowLoggerOptions();
        ConfigureCommon<JsonElement, FlowLogEntry<JsonElement>>(component, CreateLoggerNode, "Logger", "Renders structured log entries from input messages.", "list", "log", "JSON value to log.", "Structured log entry or workflow error.");
        component.AddOption<string>(ObservabilityComponentDefinition.Options.Level, OptionValueKind.Enum, "Level", "Log level applied to emitted entries.", defaultValue: defaults.Level, section: "Logging", importance: OptionDesignMetadataAttributeValues.Advanced);
        foreach (var level in Enum.GetValues<FlowLogLevel>())
            component.AddOptionChoice(ObservabilityComponentDefinition.Options.Level, level.ToString(), level.ToString());
        component.AddOption<string>(ObservabilityComponentDefinition.Options.Category, OptionValueKind.Text, "Category", "Log category included in emitted entries.", defaultValue: defaults.Category, section: "Logging", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(ObservabilityComponentDefinition.Options.MessageTemplate, OptionValueKind.MultilineText, "Message Template", "Template rendered with selected attributes, inputType, category, level, sequence, and input.", section: "Logging", importance: OptionDesignMetadataAttributeValues.Primary);
        component.AddOption<string[]>(ObservabilityComponentDefinition.Options.AttributeSelectors, OptionValueKind.Json, "Attribute Selectors", "Array of selector names resolved from host-owned attribute:{name} resources.", defaultValue: defaults.AttributeSelectors, section: "Attributes", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Json, relatedResource: ObservabilityComponentDefinition.Resources.AttributeSelectorPrefix + "{name}");
        AddCapacity(component, defaults.BoundedCapacity);
        component.AddResource<TimeProvider>(ObservabilityComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic observability timestamps and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        component.AddResource<IObservabilityValueSelector<JsonElement>>(ObservabilityComponentDefinition.Resources.AttributeSelectorPrefix + "{name}", "Attribute Selector", 1, "Required keyed selector pattern for each configured attributeSelectors entry.", designValueType: "IObservabilityValueSelector<JsonElement>", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Selector, keyPattern: ObservabilityComponentDefinition.Resources.AttributeSelectorPrefix + "{name}", option: ObservabilityComponentDefinition.Options.AttributeSelectors);
    }

    private static void ConfigureMetrics(ComponentRegistrationBuilder component)
    {
        var defaults = new FlowMetricsOptions();
        ConfigureCommon<JsonElement, FlowMetricSnapshot>(component, CreateMetricsNode, "Metrics", "Tracks count, rate, timestamp, and optional size snapshots for inputs.", "activity", "observeMetrics", "JSON value to observe.", "Metric snapshot or workflow error.");
        component.AddOption<string>(ObservabilityComponentDefinition.Options.Name, OptionValueKind.Text, "Name", "Optional metric name included in snapshots and diagnostics.", section: "Metrics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        AddCapacity(component, defaults.BoundedCapacity);
        component.AddResource<IObservabilityValueSelector<JsonElement>>(ObservabilityComponentDefinition.Resources.SizeSelector, "Size Selector", 0, "Optional keyed selector used to calculate message size metrics.", designValueType: "IObservabilityValueSelector<JsonElement>", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Selector, keyPattern: "selector:{name}");
        component.AddResource<TimeProvider>(ObservabilityComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic observability timestamps and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
    }

    private static void ConfigureCommon<TInput, TOutput>(ComponentRegistrationBuilder component, ComponentFactory factory, string displayName, string summary, string iconKey, string preferredNodeName, string inputSummary, string outputSummary)
    {
        component.UseFactory(factory);
        component.WithDisplay(displayName, "Observability", summary, iconKey, preferredNodeName, 460);
        component.AddInput<TInput>(ObservabilityComponentDefinition.Ports.Input, "Input", "Messages", 0, inputSummary, true);
        component.AddOutput<TOutput>(ObservabilityComponentDefinition.Ports.Output, "Output", "Results", 1, outputSummary, true);
    }

    private static void AddCapacity(ComponentRegistrationBuilder component, int defaultValue)
        => component.AddOption<int>(ObservabilityComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaultValue, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

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
