using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Projections;

public sealed class ProjectionsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        new()
        {
            Type = ProjectionsComponentTypes.EventProjection,
            DisplayName = "Event Projection",
            Category = "Projections",
            Summary = "Projects matching engine events into rolling projection snapshots.",
            IconKey = "projections",
            PreferredNodeName = "eventProjection",
            SuggestedEditorWidth = 480,
            Options =
            [
                Text("name", "Name"),
                Json("filter", "Filter"),
                Number("rateWindowSeconds", "Rate window s", 60, 1),
                Boolean("emitEveryMatch", "Emit every match", true),
                Boolean("emitFinalSnapshot", "Emit final snapshot", false),
                Number("maxPreviewChars", "Max preview chars", 256, 0),
                Number("boundedCapacity", "Capacity", 128, 1)
            ],
            Ports =
            [
                Port(ProjectionsComponentPorts.Input, PortDirection.Input, "FlowEvent", true),
                Port(ProjectionsComponentPorts.Output, PortDirection.Output, "EventProjectionSnapshot", true, 1),
                Port(ProjectionsComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]
        }
    ];

    private static OptionDesignMetadata Text(string name, string displayName) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName
    };

    private static OptionDesignMetadata Json(string name, string displayName) => new()
    {
        Name = name,
        Kind = OptionValueKind.Json,
        DisplayName = displayName
    };

    private static OptionDesignMetadata Boolean(string name, string displayName, bool defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Boolean,
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
