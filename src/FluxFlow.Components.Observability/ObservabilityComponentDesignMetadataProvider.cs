using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Observability;

public sealed class ObservabilityComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        Metadata(ObservabilityComponentTypes.Logger, "Logger", "logger", "Creates log entries from observed input values.",
            LoggerOptions(), ObservabilityComponentPorts.Entries, "FlowLogEntry"),
        Metadata(ObservabilityComponentTypes.Metrics, "Flow Metrics", "flowMetrics", "Creates metric snapshots from observed input values.",
            MetricsOptions(), ObservabilityComponentPorts.Snapshots, "FlowMetricSnapshot"),
        Metadata(ObservabilityComponentTypes.Counter, "Counter", "counter", "Counts observed input values and emits counter snapshots.",
            CounterOptions(), ObservabilityComponentPorts.Snapshots, "FlowCounterSnapshot")
    ];

    private static ComponentDesignMetadata Metadata(
        NodeType type,
        string displayName,
        string preferredName,
        string summary,
        IReadOnlyList<OptionDesignMetadata> options,
        string outputPort,
        string outputType) => new()
        {
            Type = type,
            DisplayName = displayName,
            Category = "Observability",
            Summary = summary,
            IconKey = "observability",
            PreferredNodeName = preferredName,
            SuggestedEditorWidth = 480,
            Options = options,
            Ports =
            [
                Port(ObservabilityComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port(outputPort, PortDirection.Output, outputType, true, 1),
                Port(ObservabilityComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]
        };

    private static IReadOnlyList<OptionDesignMetadata> CounterOptions() =>
    [
        Text("inputType", "Input type", "object"),
        Text("name", "Name", "counter"),
        Text("engine", "Expression engine"),
        Expression("predicate", "Predicate"),
        Expression("expression", "Expression"),
        Text("expressionId", "Expression id"),
        Text("expressionName", "Expression name"),
        Number("boundedCapacity", "Capacity", 128, 1)
    ];

    private static IReadOnlyList<OptionDesignMetadata> LoggerOptions() =>
    [
        Text("inputType", "Input type", "object"),
        Level(),
        Text("category", "Category", "workflow"),
        Text("messageTemplate", "Message template", "Observed {inputType} item #{sequence}."),
        Json("attributeSelectors", "Attribute selectors"),
        Number("boundedCapacity", "Capacity", 128, 1)
    ];

    private static IReadOnlyList<OptionDesignMetadata> MetricsOptions() =>
    [
        Text("inputType", "Input type", "object"),
        Text("name", "Name", "metrics"),
        Text("sizeSelector", "Size selector"),
        Number("boundedCapacity", "Capacity", 128, 1)
    ];

    private static OptionDesignMetadata Level() => new()
    {
        Name = "level",
        Kind = OptionValueKind.Enum,
        DisplayName = "Level",
        DefaultValue = "Information",
        Choices =
        [
            new() { Value = "Trace" },
            new() { Value = "Debug" },
            new() { Value = "Information" },
            new() { Value = "Warning" },
            new() { Value = "Error" },
            new() { Value = "Critical" }
        ]
    };

    private static OptionDesignMetadata Text(string name, string displayName, object? defaultValue = null) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName,
        DefaultValue = defaultValue
    };

    private static OptionDesignMetadata Expression(string name, string displayName) => new()
    {
        Name = name,
        Kind = OptionValueKind.Expression,
        DisplayName = displayName
    };

    private static OptionDesignMetadata Json(string name, string displayName) => new()
    {
        Name = name,
        Kind = OptionValueKind.Json,
        DisplayName = displayName
    };

    private static OptionDesignMetadata Number(string name, string displayName, object defaultValue, double min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        Min = min
    };

    private static PortDesignMetadata Port(string name, PortDirection direction, string valueType, bool primary, int order = 0) => new()
    {
        Name = new PortName(name),
        Direction = direction,
        ValueType = valueType,
        IsPrimary = primary,
        Order = order
    };
}
