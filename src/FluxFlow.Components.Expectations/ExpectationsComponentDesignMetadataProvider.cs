using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Expectations;

public sealed class ExpectationsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        Metadata(ExpectationsComponentTypes.Expect, "Event Expect", "eventExpect",
            "Resolves satisfied when a matching engine event is observed before timeout or completion."),
        Metadata(ExpectationsComponentTypes.Guard, "Event Guard", "eventGuard",
            "Resolves satisfied when no matching engine event is observed before timeout or completion.")
    ];

    private static ComponentDesignMetadata Metadata(
        NodeType type,
        string displayName,
        string preferredName,
        string summary) => new()
        {
            Type = type,
            DisplayName = displayName,
            Category = "Expectations",
            Summary = summary,
            IconKey = "expectations",
            PreferredNodeName = preferredName,
            SuggestedEditorWidth = 480,
            Options =
            [
                Text("name", "Name"),
                Json("filter", "Filter"),
                Number("timeoutMilliseconds", "Timeout ms", null, 1),
                Number("maxObservedEvents", "Max observed events", 10, 0),
                Number("maxPreviewChars", "Max preview chars", 256, 0),
                Number("boundedCapacity", "Capacity", 128, 1)
            ],
            Ports =
            [
                Port(ExpectationsComponentPorts.Input, PortDirection.Input, "FlowEvent", true),
                Port(ExpectationsComponentPorts.Result, PortDirection.Output, "EventExpectationResult", true, 1),
                Port(ExpectationsComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]
        };

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

    private static OptionDesignMetadata Number(string name, string displayName, object? defaultValue, double min) => new()
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
