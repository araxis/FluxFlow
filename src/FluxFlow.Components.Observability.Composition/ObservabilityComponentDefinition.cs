using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Observability.Composition;

public static partial class ObservabilityComponentDefinition
{
    private static readonly FlowCounterOptions CounterDefaults = new();
    private static readonly FlowLoggerOptions LoggerDefaults = new();
    private static readonly FlowMetricsOptions MetricsDefaults = new();

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        =>
        [
            CreateCounterMetadata(),
            CreateLoggerMetadata(),
            CreateMetricsMetadata()
        ];

    private static ComponentDesignMetadata CreateCounterMetadata()
    {
        var builder = CreateObservabilityMetadataBuilder(
            ObservabilityComponentDefinition.Types.Counter,
            "Counter",
            "Counts accepted input messages and emits counter snapshots.",
            "hash",
            "count");

        AddCounterOptions(builder);
        AddCounterResources(builder);
        AddTransformPorts(
            builder,
            "JsonElement",
            "JSON value to count.",
            nameof(FlowCounterSnapshot),
            "Current counter snapshot or workflow error.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateLoggerMetadata()
    {
        var builder = CreateObservabilityMetadataBuilder(
            ObservabilityComponentDefinition.Types.Logger,
            "Logger",
            "Renders structured log entries from input messages.",
            "list",
            "log");

        AddLoggerOptions(builder);
        AddLoggerResources(builder);
        AddTransformPorts(
            builder,
            "JsonElement",
            "JSON value to log.",
            "FlowLogEntry<JsonElement>",
            "Structured log entry or workflow error.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateMetricsMetadata()
    {
        var builder = CreateObservabilityMetadataBuilder(
            ObservabilityComponentDefinition.Types.Metrics,
            "Metrics",
            "Tracks count, rate, timestamp, and optional size snapshots for inputs.",
            "activity",
            "observeMetrics");

        AddMetricsOptions(builder);
        AddMetricsResources(builder);
        AddTransformPorts(
            builder,
            "JsonElement",
            "JSON value to observe.",
            nameof(FlowMetricSnapshot),
            "Metric snapshot or workflow error.");

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
                Options.Name,
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional counter name included in snapshots and diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Counter",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.Predicate,
                OptionValueKind.Expression,
                displayName: "Predicate",
                helperText: "Optional boolean expression used to accept or reject inputs.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Filtering",
                    importance: OptionDesignMetadataAttributeValues.Primary,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: OptionDesignMetadataAttributeValues.Expression,
                    relatedResource: ObservabilityComponentDefinition.Resources.Engine))
            .AddOption(
                Options.ExpressionId,
                OptionValueKind.Text,
                displayName: "Expression ID",
                helperText: "Optional diagnostic identifier emitted with counter diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.ExpressionName,
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
                Options.Level,
                OptionValueKind.Enum,
                displayName: "Level",
                helperText: "Log level applied to emitted entries.",
                defaultValue: LoggerDefaults.Level,
                choices: LogLevelChoices(),
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Logging",
                    importance: OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.Category,
                OptionValueKind.Text,
                displayName: "Category",
                helperText: "Log category included in emitted entries.",
                defaultValue: LoggerDefaults.Category,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Logging",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.MessageTemplate,
                OptionValueKind.MultilineText,
                displayName: "Message Template",
                helperText: "Template rendered with selected attributes, inputType, category, level, sequence, and input.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Logging",
                    importance: OptionDesignMetadataAttributeValues.Primary))
            .AddOption(
                Options.AttributeSelectors,
                OptionValueKind.Json,
                displayName: "Attribute Selectors",
                helperText: "Array of selector names resolved from host-owned attribute:{name} resources.",
                defaultValue: LoggerDefaults.AttributeSelectors,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Attributes",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Json,
                    relatedResource: ObservabilityComponentDefinition.Resources.AttributeSelectorPrefix + "{name}"))
            .AddOption(BoundedCapacityOption(LoggerDefaults.BoundedCapacity));

    private static void AddMetricsOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                Options.Name,
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
                ObservabilityComponentDefinition.Resources.Engine,
                displayName: "Expression Engine",
                order: 0,
                summary: "Conditionally required keyed expression engine when predicate is configured.",
                valueType: nameof(IFlowExpressionEngine),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ExpressionEngine,
                    keyPattern: "expression-engine:{name}",
                    requiredWhenAnyOption: Options.Predicate))
            .AddResource(
                ObservabilityComponentDefinition.Resources.ContextFactory,
                displayName: "Context Factory",
                order: 1,
                summary: "Optional keyed mapping context factory used when evaluating counter predicates.",
                valueType: "IFlowMapContextFactory<JsonElement>",
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ContextFactory,
                    keyPattern: "context-factory:{name}"))
            .AddResource(
                ObservabilityComponentDefinition.Resources.Clock,
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
                ObservabilityComponentDefinition.Resources.Clock,
                displayName: "Clock",
                order: 0,
                summary: "Optional keyed clock for deterministic observability timestamps and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "clock:{name}"))
            .AddResource(
                ObservabilityComponentDefinition.Resources.AttributeSelectorPrefix + "{name}",
                displayName: "Attribute Selector",
                order: 1,
                summary: "Required keyed selector pattern for each configured attributeSelectors entry.",
                valueType: "IObservabilityValueSelector<JsonElement>",
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Selector,
                    keyPattern: ObservabilityComponentDefinition.Resources.AttributeSelectorPrefix + "{name}",
                    option: Options.AttributeSelectors));

    private static void AddMetricsResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                ObservabilityComponentDefinition.Resources.SizeSelector,
                displayName: "Size Selector",
                order: 0,
                summary: "Optional keyed selector used to calculate message size metrics.",
                valueType: "IObservabilityValueSelector<JsonElement>",
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Selector,
                    keyPattern: "selector:{name}"))
            .AddResource(
                ObservabilityComponentDefinition.Resources.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic observability timestamps and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "clock:{name}"));

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue)
        => OptionDesignMetadataFactory.BoundedCapacity(defaultValue);

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
                ObservabilityComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: inputSummary,
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                ObservabilityComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: outputSummary,
                valueType: outputType,
                isPrimary: true);


    public static class Options
    {
        public const string Name = "name";
        public const string Predicate = "predicate";
        public const string ExpressionId = "expressionId";
        public const string ExpressionName = "expressionName";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Level = "level";
        public const string Category = "category";
        public const string MessageTemplate = "messageTemplate";
        public const string AttributeSelectors = "attributeSelectors";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Counter =>
            [
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<string>(Options.Predicate),
                ComponentOptions.Metadata<string>(Options.ExpressionId),
                ComponentOptions.Metadata<string>(Options.ExpressionName),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Logger =>
            [
                ComponentOptions.Metadata<string>(Options.Level),
                ComponentOptions.Metadata<string>(Options.Category),
                ComponentOptions.Metadata<string>(Options.MessageTemplate),
                ComponentOptions.Metadata<string[]>(Options.AttributeSelectors),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Metrics =>
            [
                ComponentOptions.Metadata<string>(Options.Name),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Counter =>
            [
                ComponentResources.Metadata<IFlowExpressionEngine>(Resources.Engine),
                ComponentResources.Metadata<IFlowMapContextFactory<JsonElement>>(Resources.ContextFactory),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Logger =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock),
                ComponentResources.Metadata<IObservabilityValueSelector<JsonElement>>(Resources.AttributeSelectorPrefix + "{name}")
            ],
            Types.Metrics =>
            [
                ComponentResources.Metadata<IObservabilityValueSelector<JsonElement>>(Resources.SizeSelector),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Counter = "metric.count";
        public const string Logger = "log.write";
        public const string Metrics = "metric.measure";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Clock = "clock";
    
        public const string Engine = "engine";
    
        public const string ContextFactory = "contextFactory";
    
        public const string SizeSelector = "sizeSelector";
    
        public const string AttributeSelectorPrefix = "attribute:";
    
        public static string AttributeSelector(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            return AttributeSelectorPrefix + name.Trim();
        }
    }
}
