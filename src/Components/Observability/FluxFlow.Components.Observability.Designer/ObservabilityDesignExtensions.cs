using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Observability.Composition;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Observability.Designer;

public static class ObservabilityDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddObservabilityDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(ObservabilityComponentDefinition.Types.Counter, DescribeCounter)
            .AddDesignMetadata(ObservabilityComponentDefinition.Types.Logger, DescribeLogger)
            .AddDesignMetadata(ObservabilityComponentDefinition.Types.Metrics, DescribeMetrics);
    }

    private static ComponentDesignMetadata DescribeCounter(ComponentDesignMetadata metadata)
    {
        var defaults = new FlowCounterOptions();
        metadata = metadata.WithDisplay("Counter", "Observability", "Counts accepted input messages and emits counter snapshots.", "hash", "count", 460);
        metadata = metadata
            .WithPort(ObservabilityComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "JSON value to count.", true)
            .WithPort(ObservabilityComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Current counter snapshot or workflow error.", true)
            .WithPort(ObservabilityComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort counter diagnostics.");
        metadata = metadata.WithOption(ObservabilityComponentDefinition.Options.Predicate, OptionValueKind.Expression, "Predicate", "Optional boolean expression used to accept or reject inputs.", section: "Filtering", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Expression, syntax: OptionDesignMetadataAttributeValues.Expression, relatedResource: ObservabilityComponentDefinition.Resources.Engine);
        metadata = metadata.WithOption(ObservabilityComponentDefinition.Options.ExpressionId, OptionValueKind.Text, "Expression ID", "Optional diagnostic identifier emitted with counter diagnostics.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(ObservabilityComponentDefinition.Options.ExpressionName, OptionValueKind.Text, "Expression Name", "Optional diagnostic name emitted with counter diagnostics.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(ObservabilityComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithResource(ObservabilityComponentDefinition.Resources.Engine, "Expression Engine", 0, "Conditionally required keyed expression engine when predicate is configured.", designValueType: nameof(IFlowExpressionEngine), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.ExpressionEngine, keyPattern: "expression-engine:{name}", requiredWhenAnyOption: ObservabilityComponentDefinition.Options.Predicate);
        metadata = metadata.WithResource(ObservabilityComponentDefinition.Resources.ContextFactory, "Context Factory", 1, "Optional keyed mapping context factory used when evaluating counter predicates.", designValueType: "IFlowMapContextFactory<JsonElement>", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.ContextFactory, keyPattern: "context-factory:{name}");
        metadata = metadata.WithResource(ObservabilityComponentDefinition.Resources.Clock, "Clock", 2, "Optional keyed clock for deterministic observability timestamps and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeLogger(ComponentDesignMetadata metadata)
    {
        var defaults = new FlowLoggerOptions();
        metadata = metadata.WithDisplay("Logger", "Observability", "Renders structured log entries from input messages.", "list", "log", 460);
        metadata = metadata
            .WithPort(ObservabilityComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "JSON value to log.", true)
            .WithPort(ObservabilityComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Structured log entry or workflow error.", true)
            .WithPort(ObservabilityComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort logger diagnostics.");
        metadata = metadata.WithOption(ObservabilityComponentDefinition.Options.Level, OptionValueKind.Enum, "Level", "Log level applied to emitted entries.", defaultValue: defaults.Level, section: "Logging", importance: OptionDesignMetadataAttributeValues.Advanced);
        foreach (var level in Enum.GetValues<FlowLogLevel>())
            metadata = metadata.WithOptionChoice(ObservabilityComponentDefinition.Options.Level, level.ToString(), level.ToString());
        metadata = metadata.WithOption(ObservabilityComponentDefinition.Options.Category, OptionValueKind.Text, "Category", "Log category included in emitted entries.", defaultValue: defaults.Category, section: "Logging", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(ObservabilityComponentDefinition.Options.MessageTemplate, OptionValueKind.MultilineText, "Message Template", "Template rendered with selected attributes, inputType, category, level, sequence, and input.", section: "Logging", importance: OptionDesignMetadataAttributeValues.Primary);
        metadata = metadata.WithOption(ObservabilityComponentDefinition.Options.AttributeSelectors, OptionValueKind.Json, "Attribute Selectors", "Array of selector names resolved from host-owned attribute:{name} resources.", defaultValue: defaults.AttributeSelectors, section: "Attributes", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Json, relatedResource: ObservabilityComponentDefinition.Resources.AttributeSelector("{name}"));
        metadata = metadata.WithOption(ObservabilityComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithResource(ObservabilityComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic observability timestamps and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        metadata = metadata.WithResource(ObservabilityComponentDefinition.Resources.AttributeSelector("{name}"), "Attribute Selector", 1, "Required keyed selector pattern for each configured attributeSelectors entry.", designValueType: "IObservabilityValueSelector<JsonElement>", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Selector, keyPattern: ObservabilityComponentDefinition.Resources.AttributeSelector("{name}"), option: ObservabilityComponentDefinition.Options.AttributeSelectors);
        return metadata;
    }

    private static ComponentDesignMetadata DescribeMetrics(ComponentDesignMetadata metadata)
    {
        var defaults = new FlowMetricsOptions();
        metadata = metadata.WithDisplay("Metrics", "Observability", "Tracks count, rate, timestamp, and optional size snapshots for inputs.", "activity", "observeMetrics", 460);
        metadata = metadata
            .WithPort(ObservabilityComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "JSON value to observe.", true)
            .WithPort(ObservabilityComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Metric snapshot or workflow error.", true)
            .WithPort(ObservabilityComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort metrics diagnostics.");
        metadata = metadata.WithOption(ObservabilityComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithResource(ObservabilityComponentDefinition.Resources.SizeSelector, "Size Selector", 0, "Optional keyed selector used to calculate message size metrics.", designValueType: "IObservabilityValueSelector<JsonElement>", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Selector, keyPattern: "selector:{name}");
        metadata = metadata.WithResource(ObservabilityComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic observability timestamps and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        return metadata;
    }
}
