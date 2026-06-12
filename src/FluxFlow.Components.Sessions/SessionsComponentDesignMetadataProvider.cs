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
            RecorderOptions(),
            [
                Port(SessionsComponentPorts.Input, PortDirection.Input, "SessionRecordInput", true),
                Port(SessionsComponentPorts.Output, PortDirection.Output, "SessionRecord", true, 1),
                Port(SessionsComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]),
        Metadata(SessionsComponentTypes.Replay, "Session Replay", "sessionReplay",
            "Replays records from a host-provided session store.",
            ReplayOptions(),
            [
                Port(SessionsComponentPorts.Output, PortDirection.Output, "SessionRecord", true),
                Port(SessionsComponentPorts.Errors, PortDirection.Output, "FlowError", false, 1)
            ]),
        Metadata(SessionsComponentTypes.Query, "Session Query", "sessionQuery",
            "Queries session metadata from a host-provided session store.",
            QueryOptions(),
            [
                Port(SessionsComponentPorts.Input, PortDirection.Input, "SessionQueryRequest", true),
                Port(SessionsComponentPorts.Output, PortDirection.Output, "SessionQueryResult", true, 1),
                Port(SessionsComponentPorts.Sessions, PortDirection.Output, "SessionMetadata", false, 2),
                Port(SessionsComponentPorts.Errors, PortDirection.Output, "FlowError", false, 3)
            ])
    ];

    private static ComponentDesignMetadata Metadata(
        NodeType type,
        string displayName,
        string preferredName,
        string summary,
        IReadOnlyList<OptionDesignMetadata> options,
        IReadOnlyList<PortDesignMetadata> ports) => new()
        {
            Type = type,
            DisplayName = displayName,
            Category = "Sessions",
            Summary = summary,
            IconKey = "sessions",
            PreferredNodeName = preferredName,
            SuggestedEditorWidth = 480,
            Options = options,
            Ports = ports
        };

    private static IReadOnlyList<OptionDesignMetadata> RecorderOptions() =>
    [
        Text("store", "Store"),
        Text("sessionId", "Session id"),
        Text("name", "Name"),
        Text("notes", "Notes"),
        Json("tags", "Tags"),
        Number("boundedCapacity", "Capacity", 128, 1)
    ];

    private static IReadOnlyList<OptionDesignMetadata> ReplayOptions() =>
    [
        Text("store", "Store"),
        Text("sessionId", "Session id", required: true),
        Mode(),
        Number("startSequence", "Start sequence", null, 1),
        Number("maxMessages", "Max messages", null, 1),
        Number("fixedIntervalMilliseconds", "Fixed interval ms", 1000, 0),
        Number("speedMultiplier", "Speed multiplier", 1, 0),
        Number("boundedCapacity", "Capacity", 128, 1)
    ];

    private static IReadOnlyList<OptionDesignMetadata> QueryOptions() =>
    [
        Text("store", "Store"),
        Text("name", "Name"),
        Text("namePrefix", "Name prefix"),
        Json("tags", "Tags"),
        Boolean("includeActive", "Include active", true),
        Boolean("includeCompleted", "Include completed", true),
        Number("limit", "Limit", 100, 1),
        Boolean("emitSessionsInResult", "Emit sessions in result", true),
        Boolean("emitSessionOutputs", "Emit session outputs", true),
        Number("boundedCapacity", "Capacity", 128, 1)
    ];

    private static OptionDesignMetadata Mode() => new()
    {
        Name = "mode",
        Kind = OptionValueKind.Enum,
        DisplayName = "Mode",
        DefaultValue = "Instant",
        Choices =
        [
            new() { Value = "Instant", DisplayName = "Instant" },
            new() { Value = "RealTime", DisplayName = "Real time" },
            new() { Value = "FixedInterval", DisplayName = "Fixed interval" },
            new() { Value = "Multiplier", DisplayName = "Multiplier" }
        ]
    };

    private static OptionDesignMetadata Text(string name, string displayName, bool required = false) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName,
        IsRequired = required
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
