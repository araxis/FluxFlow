using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Observability.Composition;

public sealed class ObservabilityComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly FlowCounterOptions CounterDefaults = new();
    private static readonly FlowLoggerOptions LoggerDefaults = new();
    private static readonly FlowMetricsOptions MetricsDefaults = new();

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreateCounterMetadata(),
            CreateLoggerMetadata(),
            CreateMetricsMetadata()
        ];

    private static ComponentDesignMetadata CreateCounterMetadata()
    {
        var builder = CreateObservabilityMetadataBuilder(
            ObservabilityCompositionNodeTypes.Counter,
            "Counter",
            "Counts accepted input messages and emits counter snapshots.",
            "hash",
            "count")
            .AddAttribute(ComponentDesignMetadataAttributeNames.Aliases, string.Join(',', ObservabilityCompositionNodeTypes.CounterDescriptor.Aliases));

        AddCounterOptions(builder);
        AddCounterResources(builder);
        AddTransformPorts(
            builder,
            nameof(FlowValue),
            "Workflow value to count.",
            "FlowResult<FlowCounterSnapshot>",
            "Counted, rejected, or failed counter result.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateLoggerMetadata()
    {
        var builder = CreateObservabilityMetadataBuilder(
            ObservabilityCompositionNodeTypes.Logger,
            "Logger",
            "Renders structured log entries from input messages.",
            "list",
            "log")
            .AddAttribute(ComponentDesignMetadataAttributeNames.Aliases, string.Join(',', ObservabilityCompositionNodeTypes.LoggerDescriptor.Aliases));

        AddLoggerOptions(builder);
        AddLoggerResources(builder);
        AddTransformPorts(
            builder,
            nameof(FlowValue),
            "Workflow value to log.",
            "FlowResult<FlowLogEntry>",
            "Complete, partial, or failed structured log result.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateMetricsMetadata()
    {
        var builder = CreateObservabilityMetadataBuilder(
            ObservabilityCompositionNodeTypes.Metrics,
            "Metrics",
            "Tracks count, rate, timestamp, and optional size snapshots for inputs.",
            "activity",
            "observeMetrics")
            .AddAttribute(ComponentDesignMetadataAttributeNames.Aliases, string.Join(',', ObservabilityCompositionNodeTypes.MetricsDescriptor.Aliases));

        AddMetricsOptions(builder);
        AddMetricsResources(builder);
        AddTransformPorts(
            builder,
            nameof(FlowValue),
            "Workflow value to observe.",
            "FlowResult<FlowMetricSnapshot>",
            "Complete, partial, or failed metric snapshot result.");

        return builder.Build();
    }

    private static ComponentDesignMetadataBuilder CreateObservabilityMetadataBuilder(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName)
        => new ComponentDesignMetadataBuilder(type)
            .WithDisplay(
                displayName: displayName,
                category: "Observability",
                summary: summary,
                iconKey: iconKey,
                preferredNodeName: preferredNodeName,
                suggestedEditorWidth: 460);

    private static void AddCounterOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                "name",
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional counter name included in snapshots and diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Counter",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "predicate",
                OptionValueKind.Expression,
                displayName: "Predicate",
                helperText: "Optional boolean expression used to accept or reject inputs.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Filtering",
                    importance: OptionDesignMetadataAttributeValues.Primary,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: OptionDesignMetadataAttributeValues.Expression,
                    relatedResource: ObservabilityCompositionResourceNames.Engine))
            .AddOption(
                "expression",
                OptionValueKind.Expression,
                displayName: "Expression",
                helperText: "Compatibility alias used when predicate is not configured.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Filtering",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: OptionDesignMetadataAttributeValues.Expression,
                    relatedResource: ObservabilityCompositionResourceNames.Engine))
            .AddOption(
                "expressionId",
                OptionValueKind.Text,
                displayName: "Expression ID",
                helperText: "Optional diagnostic identifier emitted with counter diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "expressionName",
                OptionValueKind.Text,
                displayName: "Expression Name",
                helperText: "Optional diagnostic name emitted with counter diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(BoundedCapacityOption(CounterDefaults.BoundedCapacity));

    private static void AddLoggerOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                "level",
                OptionValueKind.Enum,
                displayName: "Level",
                helperText: "Log level applied to emitted entries.",
                defaultValue: LoggerDefaults.Level,
                choices: LogLevelChoices(),
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Logging",
                    importance: OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "category",
                OptionValueKind.Text,
                displayName: "Category",
                helperText: "Log category included in emitted entries.",
                defaultValue: LoggerDefaults.Category,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Logging",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "messageTemplate",
                OptionValueKind.MultilineText,
                displayName: "Message Template",
                helperText: "Template rendered with selected attributes, inputType, category, level, sequence, and input.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Logging",
                    importance: OptionDesignMetadataAttributeValues.Primary))
            .AddOption(
                "attributeSelectors",
                OptionValueKind.Json,
                displayName: "Attribute Selectors",
                helperText: "Array of selector names resolved from host-owned attribute:{name} resources.",
                defaultValue: LoggerDefaults.AttributeSelectors,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Attributes",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Json,
                    relatedResource: ObservabilityCompositionResourceNames.AttributeSelectorPrefix + "{name}"))
            .AddOption(BoundedCapacityOption(LoggerDefaults.BoundedCapacity));

    private static void AddMetricsOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                "name",
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional metric name included in snapshots and diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Metrics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(BoundedCapacityOption(MetricsDefaults.BoundedCapacity));

    private static void AddCounterResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                ObservabilityCompositionResourceNames.Engine,
                displayName: "Expression Engine",
                order: 0,
                summary: "Conditionally required keyed expression engine when predicate or expression is configured.",
                valueType: nameof(IFlowExpressionEngine),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ExpressionEngine,
                    keyPattern: "expression-engine:{name}",
                    requiredWhenAnyOption: "predicate,expression"))
            .AddResource(
                ObservabilityCompositionResourceNames.ContextFactory,
                displayName: "Context Factory",
                order: 1,
                summary: "Optional keyed mapping context factory used when evaluating counter predicates.",
                valueType: "IFlowMapContextFactory<FlowValue>",
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ContextFactory,
                    keyPattern: "context-factory:{name}"))
            .AddResource(
                ObservabilityCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 2,
                summary: "Optional keyed clock for deterministic observability timestamps and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "clock:{name}"));

    private static void AddLoggerResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                ObservabilityCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 0,
                summary: "Optional keyed clock for deterministic observability timestamps and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "clock:{name}"))
            .AddResource(
                ObservabilityCompositionResourceNames.AttributeSelectorPrefix + "{name}",
                displayName: "Attribute Selector",
                order: 1,
                summary: "Required keyed selector pattern for each configured attributeSelectors entry.",
                valueType: nameof(IObservabilityValueSelector),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Selector,
                    keyPattern: ObservabilityCompositionResourceNames.AttributeSelectorPrefix + "{name}",
                    option: "attributeSelectors"));

    private static void AddMetricsResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                ObservabilityCompositionResourceNames.SizeSelector,
                displayName: "Size Selector",
                order: 0,
                summary: "Optional keyed selector used to calculate message size metrics.",
                valueType: nameof(IObservabilityValueSelector),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Selector,
                    keyPattern: "selector:{name}"))
            .AddResource(
                ObservabilityCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic observability timestamps and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "clock:{name}"));

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue) => new()
    {
        Name = new ComponentOptionName("boundedCapacity"),
        Kind = OptionValueKind.Number,
        DisplayName = new ComponentMetadataText("Bounded Capacity"),
        DefaultValue = defaultValue,
        Min = 1,
        HelperText = new ComponentMetadataText("Maximum queued input messages."),
        Attributes = OptionDesignMetadataAttributes.CreateMap(
            section: "Runtime",
            importance: OptionDesignMetadataAttributeValues.Advanced,
            editor: OptionDesignMetadataAttributeValues.Number)
    };

    private static IReadOnlyList<OptionChoiceMetadata> LogLevelChoices()
        =>
        [
            LogLevelChoice(FlowLogLevel.Trace),
            LogLevelChoice(FlowLogLevel.Debug),
            LogLevelChoice(FlowLogLevel.Information),
            LogLevelChoice(FlowLogLevel.Warning),
            LogLevelChoice(FlowLogLevel.Error),
            LogLevelChoice(FlowLogLevel.Critical)
        ];

    private static OptionChoiceMetadata LogLevelChoice(FlowLogLevel level) => new()
    {
        Value = new ComponentOptionChoiceValue(level.ToString()),
        DisplayName = new ComponentMetadataText(level.ToString())
    };

    private static void AddTransformPorts(
        ComponentDesignMetadataBuilder builder,
        string inputType,
        string inputSummary,
        string outputType,
        string outputSummary)
        => builder
            .AddInputPort(
                ObservabilityCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: inputSummary,
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                ObservabilityCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: outputSummary,
                valueType: outputType,
                isPrimary: true);
}
