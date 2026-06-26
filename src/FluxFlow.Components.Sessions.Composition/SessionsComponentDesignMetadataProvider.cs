using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Options;

namespace FluxFlow.Components.Sessions.Composition;

public sealed class SessionsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private const double PositiveDoubleMin = 0.000001;

    private static readonly SessionRecorderOptions RecorderDefaults = new();
    private static readonly SessionReplayOptions ReplayDefaults = new();
    private static readonly SessionQueryOptions QueryDefaults = new();

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        =>
        [
            CreateRecorderMetadata(),
            CreateReplayMetadata(),
            CreateQueryMetadata()
        ];

    private static ComponentDesignMetadata CreateRecorderMetadata()
        => CreateSessionMetadata(
            SessionsCompositionNodeTypes.Recorder,
            "Session Recorder",
            "Records incoming messages to a host-owned session store.",
            "history",
            "recordSession",
            [
                StoreOption(),
                SessionIdOption(isRequired: false),
                new OptionDesignMetadata
                {
                    Name = "name",
                    Kind = OptionValueKind.Text,
                    DisplayName = "Name",
                    HelperText = "Optional session name stored with session metadata."
                },
                new OptionDesignMetadata
                {
                    Name = "notes",
                    Kind = OptionValueKind.MultilineText,
                    DisplayName = "Notes",
                    HelperText = "Optional session notes stored with session metadata."
                },
                TagsOption(),
                BoundedCapacityOption(RecorderDefaults.BoundedCapacity)
            ],
            TransformPorts(
                nameof(SessionRecordInput),
                "Session record input.",
                nameof(SessionRecord),
                "Stored session record."));

    private static ComponentDesignMetadata CreateReplayMetadata()
        => CreateSessionMetadata(
            SessionsCompositionNodeTypes.Replay,
            "Session Replay",
            "Replays records from a host-owned session store as source messages.",
            "history-play",
            "replaySession",
            [
                StoreOption(),
                SessionIdOption(isRequired: true),
                new OptionDesignMetadata
                {
                    Name = "mode",
                    Kind = OptionValueKind.Enum,
                    DisplayName = "Mode",
                    DefaultValue = ReplayDefaults.Mode.ToString(),
                    HelperText = "Timing mode used between replayed records.",
                    Choices = ReplayModeChoices()
                },
                BoundedCapacityOption(ReplayDefaults.BoundedCapacity),
                new OptionDesignMetadata
                {
                    Name = "startSequence",
                    Kind = OptionValueKind.Number,
                    DisplayName = "Start Sequence",
                    Min = 1,
                    HelperText = "Optional first record sequence to replay."
                },
                new OptionDesignMetadata
                {
                    Name = "maxMessages",
                    Kind = OptionValueKind.Number,
                    DisplayName = "Max Messages",
                    Min = 1,
                    HelperText = "Optional maximum number of messages to replay."
                },
                new OptionDesignMetadata
                {
                    Name = "fixedIntervalMilliseconds",
                    Kind = OptionValueKind.Number,
                    DisplayName = "Fixed Interval Milliseconds",
                    DefaultValue = ReplayDefaults.FixedIntervalMilliseconds,
                    Min = 0,
                    HelperText = "Delay used by FixedInterval replay mode."
                },
                new OptionDesignMetadata
                {
                    Name = "speedMultiplier",
                    Kind = OptionValueKind.Number,
                    DisplayName = "Speed Multiplier",
                    DefaultValue = ReplayDefaults.SpeedMultiplier,
                    Min = PositiveDoubleMin,
                    HelperText = "Multiplier used by Multiplier replay mode; must be greater than zero."
                }
            ],
            [
                OutputPort(
                    SessionsCompositionPortNames.Output,
                    "Output",
                    "Messages",
                    0,
                    nameof(SessionRecord),
                    "Replayed session record.",
                    isPrimary: true)
            ]);

    private static ComponentDesignMetadata CreateQueryMetadata()
        => CreateSessionMetadata(
            SessionsCompositionNodeTypes.Query,
            "Session Query",
            "Queries sessions and can fan matching sessions to a separate output.",
            "history-search",
            "querySessions",
            [
                StoreOption(),
                new OptionDesignMetadata
                {
                    Name = "name",
                    Kind = OptionValueKind.Text,
                    DisplayName = "Name",
                    HelperText = "Default exact session name filter."
                },
                new OptionDesignMetadata
                {
                    Name = "namePrefix",
                    Kind = OptionValueKind.Text,
                    DisplayName = "Name Prefix",
                    HelperText = "Default session name prefix filter."
                },
                TagsOption(),
                new OptionDesignMetadata
                {
                    Name = "includeActive",
                    Kind = OptionValueKind.Boolean,
                    DisplayName = "Include Active",
                    DefaultValue = QueryDefaults.IncludeActive,
                    HelperText = "Include active sessions in query results."
                },
                new OptionDesignMetadata
                {
                    Name = "includeCompleted",
                    Kind = OptionValueKind.Boolean,
                    DisplayName = "Include Completed",
                    DefaultValue = QueryDefaults.IncludeCompleted,
                    HelperText = "Include completed sessions in query results."
                },
                new OptionDesignMetadata
                {
                    Name = "limit",
                    Kind = OptionValueKind.Number,
                    DisplayName = "Limit",
                    DefaultValue = QueryDefaults.Limit,
                    Min = 1,
                    HelperText = "Maximum number of sessions to return."
                },
                new OptionDesignMetadata
                {
                    Name = "emitSessionsInResult",
                    Kind = OptionValueKind.Boolean,
                    DisplayName = "Emit Sessions In Result",
                    DefaultValue = QueryDefaults.EmitSessionsInResult,
                    HelperText = "Include matching session metadata in the query result payload."
                },
                new OptionDesignMetadata
                {
                    Name = "emitSessionOutputs",
                    Kind = OptionValueKind.Boolean,
                    DisplayName = "Emit Session Outputs",
                    DefaultValue = QueryDefaults.EmitSessionOutputs,
                    HelperText = "Fan each matching session to the Sessions output."
                },
                BoundedCapacityOption(QueryDefaults.BoundedCapacity)
            ],
            [
                InputPort(nameof(SessionQueryRequest), "Session query request."),
                OutputPort(
                    SessionsCompositionPortNames.Output,
                    "Output",
                    "Results",
                    1,
                    nameof(SessionQueryResult),
                    "Session query result.",
                    isPrimary: true),
                OutputPort(
                    SessionsCompositionPortNames.Sessions,
                    "Sessions",
                    "Sessions",
                    2,
                    nameof(SessionMetadata),
                    "Matching session metadata.")
            ]);

    private static ComponentDesignMetadata CreateSessionMetadata(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName,
        IReadOnlyList<OptionDesignMetadata> options,
        IReadOnlyList<PortDesignMetadata> ports) => new()
        {
            Type = new ComponentType(type),
            DisplayName = displayName,
            Category = "Sessions",
            Summary = summary,
            IconKey = iconKey,
            PreferredNodeName = preferredNodeName,
            SuggestedEditorWidth = 460,
            Options = options,
            Resources = SessionResources(),
            Ports = ports
        };

    private static OptionDesignMetadata StoreOption() => new()
    {
        Name = "store",
        Kind = OptionValueKind.Text,
        DisplayName = "Store",
        HelperText = "Diagnostic store metadata; DI selection uses the required host-owned store resource."
    };

    private static OptionDesignMetadata SessionIdOption(bool isRequired) => new()
    {
        Name = "sessionId",
        Kind = OptionValueKind.Text,
        DisplayName = "Session ID",
        HelperText = isRequired
            ? "Required session identifier to replay."
            : "Optional session identifier. The store may generate one when omitted.",
        IsRequired = isRequired
    };

    private static OptionDesignMetadata TagsOption() => new()
    {
        Name = "tags",
        Kind = OptionValueKind.Json,
        DisplayName = "Tags",
        HelperText = "Optional string tag map used in session metadata or query defaults."
    };

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue) => new()
    {
        Name = "boundedCapacity",
        Kind = OptionValueKind.Number,
        DisplayName = "Bounded Capacity",
        DefaultValue = defaultValue,
        Min = 1,
        HelperText = "Maximum queued messages."
    };

    private static IReadOnlyList<OptionChoiceMetadata> ReplayModeChoices()
        =>
        [
            ReplayModeChoice(SessionReplayMode.RealTime, "Real Time", "Use timestamp deltas from stored records."),
            ReplayModeChoice(SessionReplayMode.FixedInterval, "Fixed Interval", "Use a fixed delay between records."),
            ReplayModeChoice(SessionReplayMode.Multiplier, "Multiplier", "Use timestamp deltas divided by speed multiplier."),
            ReplayModeChoice(SessionReplayMode.Instant, "Instant", "Emit records without inter-record delay.")
        ];

    private static OptionChoiceMetadata ReplayModeChoice(
        SessionReplayMode mode,
        string displayName,
        string helperText) => new()
        {
            Value = mode.ToString(),
            DisplayName = displayName,
            HelperText = helperText
        };

    private static IReadOnlyList<ResourceDesignMetadata> SessionResources()
        =>
        [
            new ResourceDesignMetadata
            {
                Name = SessionsCompositionResourceNames.Store,
                DisplayName = "Store",
                Order = 0,
                Summary = "Required keyed session store used to record, replay, or query sessions.",
                ValueType = nameof(ISessionStore),
                IsRequired = true
            },
            new ResourceDesignMetadata
            {
                Name = SessionsCompositionResourceNames.Clock,
                DisplayName = "Clock",
                Order = 1,
                Summary = "Optional keyed clock for deterministic session timestamps, replay pacing, and diagnostics.",
                ValueType = nameof(TimeProvider)
            }
        ];

    private static IReadOnlyList<PortDesignMetadata> TransformPorts(
        string inputType,
        string inputSummary,
        string outputType,
        string outputSummary)
        =>
        [
            InputPort(inputType, inputSummary),
            OutputPort(
                SessionsCompositionPortNames.Output,
                "Output",
                "Results",
                1,
                outputType,
                outputSummary,
                isPrimary: true)
        ];

    private static PortDesignMetadata InputPort(
        string valueType,
        string summary) => new()
        {
            Name = new ComponentPortName(SessionsCompositionPortNames.Input),
            Direction = PortDirection.Input,
            DisplayName = "Input",
            Group = "Messages",
            Order = 0,
            Summary = summary,
            ValueType = valueType,
            IsPrimary = true
        };

    private static PortDesignMetadata OutputPort(
        string name,
        string displayName,
        string group,
        int order,
        string valueType,
        string summary,
        bool isPrimary = false) => new()
        {
            Name = new ComponentPortName(name),
            Direction = PortDirection.Output,
            DisplayName = displayName,
            Group = group,
            Order = order,
            Summary = summary,
            ValueType = valueType,
            IsPrimary = isPrimary
        };
}
