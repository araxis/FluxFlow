using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Metrics;

public sealed class MetricsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        new()
        {
            Type = MetricsComponentTypes.Aggregate,
            DisplayName = "Metrics Aggregate",
            Category = "Metrics",
            Summary = "Aggregates metric samples into rolling metric snapshots.",
            IconKey = "metrics",
            PreferredNodeName = "metricsAggregate",
            SuggestedEditorWidth = 480,
            Options =
            [
                Number("windowMilliseconds", "Window ms", 60000, 1),
                Number("boundedCapacity", "Capacity", 128, 1)
            ],
            Ports =
            [
                Port(MetricsComponentPorts.Input, PortDirection.Input, "MetricSampleInput", true),
                Port(MetricsComponentPorts.Output, PortDirection.Output, "MetricSnapshotOutput", true, 1),
                Port(MetricsComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]
        }
    ];

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
