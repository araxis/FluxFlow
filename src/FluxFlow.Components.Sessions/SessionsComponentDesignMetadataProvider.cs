using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Sessions;

public sealed class SessionsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        Metadata(SessionsComponentTypes.Recorder, "Session Recorder", "sessionRecorder",
            "Records session input values into a host-provided session store.",
            "SessionRecordInput", SessionsComponentPorts.Output, "SessionRecord"),
        Metadata(SessionsComponentTypes.Replay, "Session Replay", "sessionReplay",
            "Replays records from a host-provided session store.",
            null, SessionsComponentPorts.Output, "SessionRecord"),
        Metadata(SessionsComponentTypes.Query, "Session Query", "sessionQuery",
            "Queries session metadata from a host-provided session store.",
            "SessionQueryRequest", SessionsComponentPorts.Sessions, "SessionQueryResult")
    ];

    private static ComponentDesignMetadata Metadata(
        NodeType type,
        string displayName,
        string preferredName,
        string summary,
        string? inputType,
        string outputPort,
        string outputType) => new()
        {
            Type = type,
            DisplayName = displayName,
            Category = "Sessions",
            Summary = summary,
            IconKey = "sessions",
            PreferredNodeName = preferredName,
            SuggestedEditorWidth = 480,
            Options =
            [
                Text("sessionId", "Session id"),
                Number("boundedCapacity", "Capacity", 128, 1)
            ],
            Ports = inputType is null
                ? [
                    Port(outputPort, PortDirection.Output, outputType, true),
                    Port(SessionsComponentPorts.Errors, PortDirection.Output, "FlowError", false, 1)
                ]
                : [
                    Port(SessionsComponentPorts.Input, PortDirection.Input, inputType, true),
                    Port(outputPort, PortDirection.Output, outputType, true, 1),
                    Port(SessionsComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
                ]
        };

    private static OptionDesignMetadata Text(string name, string displayName) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
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
