using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Observability;

public sealed class ObservabilityComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        Metadata(ObservabilityComponentTypes.Logger, "Logger", "logger", "Creates log entries from observed input values.",
            ObservabilityComponentPorts.Entries, "FlowLogEntry"),
        Metadata(ObservabilityComponentTypes.Metrics, "Flow Metrics", "flowMetrics", "Creates metric snapshots from observed input values.",
            ObservabilityComponentPorts.Snapshots, "FlowMetricSnapshot"),
        Metadata(ObservabilityComponentTypes.Counter, "Counter", "counter", "Counts observed input values and emits counter snapshots.",
            ObservabilityComponentPorts.Snapshots, "FlowCounterSnapshot")
    ];

    private static ComponentDesignMetadata Metadata(
        NodeType type,
        string displayName,
        string preferredName,
        string summary,
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
            Options =
            [
                Text("inputType", "Input type", "object"),
                Text("category", "Category", "workflow"),
                Text("messageTemplate", "Message template"),
                Number("boundedCapacity", "Capacity", 128, 1)
            ],
            Ports =
            [
                Port(ObservabilityComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port(outputPort, PortDirection.Output, outputType, true, 1)
            ]
        };

    private static OptionDesignMetadata Text(string name, string displayName, object? defaultValue = null) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName,
        DefaultValue = defaultValue
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
