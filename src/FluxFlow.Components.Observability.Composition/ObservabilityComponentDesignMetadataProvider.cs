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

    private static ComponentDesignMetadata CreateCounterMetadata() => new()
    {
        Type = new ComponentType(ObservabilityCompositionNodeTypes.Counter),
        DisplayName = "Counter",
        Category = "Observability",
        Summary = "Counts accepted input messages and emits counter snapshots.",
        IconKey = "hash",
        PreferredNodeName = "count",
        SuggestedEditorWidth = 460,
        Options =
        [
            InputTypeOption(CounterDefaults.InputType),
            new OptionDesignMetadata
            {
                Name = "name",
                Kind = OptionValueKind.Text,
                DisplayName = "Name",
                HelperText = "Optional counter name included in snapshots and diagnostics."
            },
            new OptionDesignMetadata
            {
                Name = "engine",
                Kind = OptionValueKind.Text,
                DisplayName = "Engine",
                HelperText = "Diagnostic engine metadata; composition DI selection uses the engine resource."
            },
            new OptionDesignMetadata
            {
                Name = "predicate",
                Kind = OptionValueKind.Expression,
                DisplayName = "Predicate",
                HelperText = "Optional boolean expression used to accept or reject inputs."
            },
            new OptionDesignMetadata
            {
                Name = "expression",
                Kind = OptionValueKind.Expression,
                DisplayName = "Expression",
                HelperText = "Compatibility alias used when predicate is not configured."
            },
            new OptionDesignMetadata
            {
                Name = "expressionId",
                Kind = OptionValueKind.Text,
                DisplayName = "Expression ID",
                HelperText = "Optional diagnostic identifier emitted with counter diagnostics."
            },
            new OptionDesignMetadata
            {
                Name = "expressionName",
                Kind = OptionValueKind.Text,
                DisplayName = "Expression Name",
                HelperText = "Optional diagnostic name emitted with counter diagnostics."
            },
            BoundedCapacityOption(CounterDefaults.BoundedCapacity)
        ],
        Ports = TransformPorts(
            "TInput",
            "Input message to count.",
            nameof(FlowCounterSnapshot),
            "Counter snapshot."),
        Resources =
        [
            Resource(
                ObservabilityCompositionResourceNames.Engine,
                "Expression Engine",
                nameof(IFlowExpressionEngine),
                0,
                "Conditionally required keyed expression engine when predicate or expression is configured.",
                isRequired: false,
                attributes: new Dictionary<string, string>
                {
                    ["requiredWhenAnyOption"] = "predicate,expression"
                }),
            Resource(
                ObservabilityCompositionResourceNames.ContextFactory,
                "Context Factory",
                "IFlowMapContextFactory<TInput>",
                1,
                "Optional keyed mapping context factory used when evaluating counter predicates."),
            ClockResource(2)
        ]
    };

    private static ComponentDesignMetadata CreateLoggerMetadata() => new()
    {
        Type = new ComponentType(ObservabilityCompositionNodeTypes.Logger),
        DisplayName = "Logger",
        Category = "Observability",
        Summary = "Renders structured log entries from input messages.",
        IconKey = "list",
        PreferredNodeName = "log",
        SuggestedEditorWidth = 460,
        Options =
        [
            InputTypeOption(LoggerDefaults.InputType),
            new OptionDesignMetadata
            {
                Name = "level",
                Kind = OptionValueKind.Enum,
                DisplayName = "Level",
                DefaultValue = LoggerDefaults.Level,
                HelperText = "Log level applied to emitted entries.",
                Choices = LogLevelChoices()
            },
            new OptionDesignMetadata
            {
                Name = "category",
                Kind = OptionValueKind.Text,
                DisplayName = "Category",
                DefaultValue = LoggerDefaults.Category,
                HelperText = "Log category included in emitted entries."
            },
            new OptionDesignMetadata
            {
                Name = "messageTemplate",
                Kind = OptionValueKind.MultilineText,
                DisplayName = "Message Template",
                HelperText = "Template rendered with selected attributes, inputType, category, level, sequence, and input."
            },
            new OptionDesignMetadata
            {
                Name = "attributeSelectors",
                Kind = OptionValueKind.Json,
                DisplayName = "Attribute Selectors",
                DefaultValue = LoggerDefaults.AttributeSelectors,
                HelperText = "Array of selector names resolved from host-owned attribute:{name} resources."
            },
            BoundedCapacityOption(LoggerDefaults.BoundedCapacity)
        ],
        Ports = TransformPorts(
            "TInput",
            "Input message to log.",
            nameof(FlowLogEntry),
            "Structured log entry."),
        Resources =
        [
            ClockResource(0),
            Resource(
                ObservabilityCompositionResourceNames.AttributeSelectorPrefix + "{name}",
                "Attribute Selector",
                "IObservabilityValueSelector<TInput>",
                1,
                "Required keyed selector pattern for each configured attributeSelectors entry.",
                attributes: new Dictionary<string, string>
                {
                    ["pattern"] = "true",
                    ["option"] = "attributeSelectors"
                })
        ]
    };

    private static ComponentDesignMetadata CreateMetricsMetadata() => new()
    {
        Type = new ComponentType(ObservabilityCompositionNodeTypes.Metrics),
        DisplayName = "Metrics",
        Category = "Observability",
        Summary = "Tracks count, rate, timestamp, and optional size snapshots for inputs.",
        IconKey = "activity",
        PreferredNodeName = "observeMetrics",
        SuggestedEditorWidth = 460,
        Options =
        [
            InputTypeOption(MetricsDefaults.InputType),
            new OptionDesignMetadata
            {
                Name = "name",
                Kind = OptionValueKind.Text,
                DisplayName = "Name",
                HelperText = "Optional metric name included in snapshots and diagnostics."
            },
            new OptionDesignMetadata
            {
                Name = "sizeSelector",
                Kind = OptionValueKind.Text,
                DisplayName = "Size Selector",
                HelperText = "Diagnostic selector metadata; composition DI selection uses the sizeSelector resource."
            },
            BoundedCapacityOption(MetricsDefaults.BoundedCapacity)
        ],
        Ports = TransformPorts(
            "TInput",
            "Input message to observe.",
            nameof(FlowMetricSnapshot),
            "Metric snapshot."),
        Resources =
        [
            Resource(
                ObservabilityCompositionResourceNames.SizeSelector,
                "Size Selector",
                "IObservabilityValueSelector<TInput>",
                0,
                "Optional keyed selector used to calculate message size metrics."),
            ClockResource(1)
        ]
    };

    private static ResourceDesignMetadata ClockResource(int order) => new()
    {
        Name = ObservabilityCompositionResourceNames.Clock,
        DisplayName = "Clock",
        Order = order,
        Summary = "Optional keyed clock for deterministic observability timestamps and diagnostics.",
        ValueType = nameof(TimeProvider)
    };

    private static ResourceDesignMetadata Resource(
        string name,
        string displayName,
        string valueType,
        int order,
        string summary,
        bool isRequired = false,
        IReadOnlyDictionary<string, string>? attributes = null) => new()
        {
            Name = name,
            DisplayName = displayName,
            Order = order,
            Summary = summary,
            ValueType = valueType,
            IsRequired = isRequired,
            Attributes = attributes ?? new Dictionary<string, string>()
        };

    private static OptionDesignMetadata InputTypeOption(string defaultValue) => new()
    {
        Name = "inputType",
        Kind = OptionValueKind.Text,
        DisplayName = "Input Type",
        DefaultValue = defaultValue,
        HelperText = "Diagnostic input type metadata; CLR input type comes from the closed registration."
    };

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue) => new()
    {
        Name = "boundedCapacity",
        Kind = OptionValueKind.Number,
        DisplayName = "Bounded Capacity",
        DefaultValue = defaultValue,
        Min = 1,
        HelperText = "Maximum queued input messages."
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
        Value = level.ToString(),
        DisplayName = level.ToString()
    };

    private static IReadOnlyList<PortDesignMetadata> TransformPorts(
        string inputType,
        string inputSummary,
        string outputType,
        string outputSummary)
        =>
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ObservabilityCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = inputSummary,
                ValueType = inputType,
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(ObservabilityCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Results",
                Order = 1,
                Summary = outputSummary,
                ValueType = outputType,
                IsPrimary = true
            }
        ];
}
