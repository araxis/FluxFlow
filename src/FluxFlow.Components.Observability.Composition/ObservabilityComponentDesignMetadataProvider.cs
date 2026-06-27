using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Options;
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
            "count");

        AddCounterOptions(builder);
        AddCounterResources(builder);
        AddTransformPorts(
            builder,
            "TInput",
            "Input message to count.",
            nameof(FlowCounterSnapshot),
            "Counter snapshot.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateLoggerMetadata()
    {
        var builder = CreateObservabilityMetadataBuilder(
            ObservabilityCompositionNodeTypes.Logger,
            "Logger",
            "Renders structured log entries from input messages.",
            "list",
            "log");

        AddLoggerOptions(builder);
        AddLoggerResources(builder);
        AddTransformPorts(
            builder,
            "TInput",
            "Input message to log.",
            nameof(FlowLogEntry),
            "Structured log entry.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateMetricsMetadata()
    {
        var builder = CreateObservabilityMetadataBuilder(
            ObservabilityCompositionNodeTypes.Metrics,
            "Metrics",
            "Tracks count, rate, timestamp, and optional size snapshots for inputs.",
            "activity",
            "observeMetrics");

        AddMetricsOptions(builder);
        AddMetricsResources(builder);
        AddTransformPorts(
            builder,
            "TInput",
            "Input message to observe.",
            nameof(FlowMetricSnapshot),
            "Metric snapshot.");

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
            .AddOption(InputTypeOption(CounterDefaults.InputType))
            .AddOption(
                "name",
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional counter name included in snapshots and diagnostics.")
            .AddOption(
                "engine",
                OptionValueKind.Text,
                displayName: "Engine",
                helperText: "Diagnostic engine metadata; composition DI selection uses the engine resource.")
            .AddOption(
                "predicate",
                OptionValueKind.Expression,
                displayName: "Predicate",
                helperText: "Optional boolean expression used to accept or reject inputs.")
            .AddOption(
                "expression",
                OptionValueKind.Expression,
                displayName: "Expression",
                helperText: "Compatibility alias used when predicate is not configured.")
            .AddOption(
                "expressionId",
                OptionValueKind.Text,
                displayName: "Expression ID",
                helperText: "Optional diagnostic identifier emitted with counter diagnostics.")
            .AddOption(
                "expressionName",
                OptionValueKind.Text,
                displayName: "Expression Name",
                helperText: "Optional diagnostic name emitted with counter diagnostics.")
            .AddOption(BoundedCapacityOption(CounterDefaults.BoundedCapacity));

    private static void AddLoggerOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(InputTypeOption(LoggerDefaults.InputType))
            .AddOption(
                "level",
                OptionValueKind.Enum,
                displayName: "Level",
                helperText: "Log level applied to emitted entries.",
                defaultValue: LoggerDefaults.Level,
                choices: LogLevelChoices())
            .AddOption(
                "category",
                OptionValueKind.Text,
                displayName: "Category",
                helperText: "Log category included in emitted entries.",
                defaultValue: LoggerDefaults.Category)
            .AddOption(
                "messageTemplate",
                OptionValueKind.MultilineText,
                displayName: "Message Template",
                helperText: "Template rendered with selected attributes, inputType, category, level, sequence, and input.")
            .AddOption(
                "attributeSelectors",
                OptionValueKind.Json,
                displayName: "Attribute Selectors",
                helperText: "Array of selector names resolved from host-owned attribute:{name} resources.",
                defaultValue: LoggerDefaults.AttributeSelectors)
            .AddOption(BoundedCapacityOption(LoggerDefaults.BoundedCapacity));

    private static void AddMetricsOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(InputTypeOption(MetricsDefaults.InputType))
            .AddOption(
                "name",
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional metric name included in snapshots and diagnostics.")
            .AddOption(
                "sizeSelector",
                OptionValueKind.Text,
                displayName: "Size Selector",
                helperText: "Diagnostic selector metadata; composition DI selection uses the sizeSelector resource.")
            .AddOption(BoundedCapacityOption(MetricsDefaults.BoundedCapacity));

    private static void AddCounterResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                ObservabilityCompositionResourceNames.Engine,
                displayName: "Expression Engine",
                order: 0,
                summary: "Conditionally required keyed expression engine when predicate or expression is configured.",
                valueType: nameof(IFlowExpressionEngine),
                attributes: new Dictionary<string, string>
                {
                    ["requiredWhenAnyOption"] = "predicate,expression"
                })
            .AddResource(
                ObservabilityCompositionResourceNames.ContextFactory,
                displayName: "Context Factory",
                order: 1,
                summary: "Optional keyed mapping context factory used when evaluating counter predicates.",
                valueType: "IFlowMapContextFactory<TInput>")
            .AddResource(
                ObservabilityCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 2,
                summary: "Optional keyed clock for deterministic observability timestamps and diagnostics.",
                valueType: nameof(TimeProvider));

    private static void AddLoggerResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                ObservabilityCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 0,
                summary: "Optional keyed clock for deterministic observability timestamps and diagnostics.",
                valueType: nameof(TimeProvider))
            .AddResource(
                ObservabilityCompositionResourceNames.AttributeSelectorPrefix + "{name}",
                displayName: "Attribute Selector",
                order: 1,
                summary: "Required keyed selector pattern for each configured attributeSelectors entry.",
                valueType: "IObservabilityValueSelector<TInput>",
                attributes: new Dictionary<string, string>
                {
                    ["pattern"] = "true",
                    ["option"] = "attributeSelectors"
                });

    private static void AddMetricsResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                ObservabilityCompositionResourceNames.SizeSelector,
                displayName: "Size Selector",
                order: 0,
                summary: "Optional keyed selector used to calculate message size metrics.",
                valueType: "IObservabilityValueSelector<TInput>")
            .AddResource(
                ObservabilityCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic observability timestamps and diagnostics.",
                valueType: nameof(TimeProvider));

    private static OptionDesignMetadata InputTypeOption(string defaultValue) => new()
    {
        Name = new ComponentOptionName("inputType"),
        Kind = OptionValueKind.Text,
        DisplayName = new ComponentMetadataText("Input Type"),
        DefaultValue = defaultValue,
        HelperText = new ComponentMetadataText("Diagnostic input type metadata; CLR input type comes from the closed registration.")
    };

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue) => new()
    {
        Name = new ComponentOptionName("boundedCapacity"),
        Kind = OptionValueKind.Number,
        DisplayName = new ComponentMetadataText("Bounded Capacity"),
        DefaultValue = defaultValue,
        Min = 1,
        HelperText = new ComponentMetadataText("Maximum queued input messages.")
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
