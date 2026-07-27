using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Options;

namespace FluxFlow.Components.Sessions.Composition;

public static partial class SessionsComponentDefinition
{
    private const double PositiveDoubleMin = 0.000001;

    private static readonly SessionRecorderOptions RecorderDefaults = new();
    private static readonly SessionReplayOptions ReplayDefaults = new();
    private static readonly SessionQueryOptions QueryDefaults = new();

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        =>
        [
            CreateRecorderMetadata(),
            CreateReplayMetadata(),
            CreateQueryMetadata()
        ];

    private static ComponentDesignMetadata CreateRecorderMetadata()
    {
        var builder = CreateSessionMetadataBuilder(
            SessionsComponentDefinition.Types.Recorder,
            "Session Recorder",
            "Records incoming messages to a host-owned session store.",
            "history",
            "recordSession");

        builder
            .AddOption(SessionIdOption(isRequired: false))
            .AddOption(
                Options.SessionName,
                OptionValueKind.Text,
                displayName: "Session Name",
                helperText: "Optional session name stored with session metadata.",
                attributes: OptionAttributes(
                    "Session",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.Notes,
                OptionValueKind.MultilineText,
                displayName: "Notes",
                helperText: "Optional session notes stored with session metadata.",
                attributes: OptionAttributes(
                    "Session",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(TagsOption("Metadata"))
            .AddOption(BoundedCapacityOption(RecorderDefaults.BoundedCapacity));

        AddTransformPorts(
            builder,
            nameof(SessionContentRecordInput),
            "Exact-content session record input.",
            nameof(SessionContentRecord),
            "Stored or failed session record result.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateReplayMetadata()
    {
        var builder = CreateSessionMetadataBuilder(
            SessionsComponentDefinition.Types.Replay,
            "Session Replay",
            "Replays records from a host-owned session store as source messages.",
            "history-play",
            "replaySession");

        builder
            .AddOption(SessionIdOption(isRequired: true))
            .AddOption(
                Options.Mode,
                OptionValueKind.Enum,
                displayName: "Mode",
                helperText: "Timing mode used between replayed records.",
                defaultValue: ReplayDefaults.Mode.ToString(),
                choices: ReplayModeChoices(),
                attributes: OptionAttributes(
                    "Replay",
                    OptionDesignMetadataAttributeValues.Primary))
            .AddOption(BoundedCapacityOption(ReplayDefaults.BoundedCapacity))
            .AddOption(
                Options.StartSequence,
                OptionValueKind.Number,
                displayName: "Start Sequence",
                helperText: "Optional first record sequence to replay.",
                min: 1,
                attributes: OptionAttributes(
                    "Replay",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.MaxMessages,
                OptionValueKind.Number,
                displayName: "Max Messages",
                helperText: "Optional maximum number of messages to replay.",
                min: 1,
                attributes: OptionAttributes(
                    "Replay",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.FixedIntervalMilliseconds,
                OptionValueKind.Number,
                displayName: "Fixed Interval Milliseconds",
                helperText: "Delay used by FixedInterval replay mode.",
                defaultValue: ReplayDefaults.FixedIntervalMilliseconds,
                min: 0,
                attributes: OptionAttributes(
                    "Timing",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.SpeedMultiplier,
                OptionValueKind.Number,
                displayName: "Speed Multiplier",
                helperText: "Multiplier used by Multiplier replay mode; must be greater than zero.",
                defaultValue: ReplayDefaults.SpeedMultiplier,
                min: PositiveDoubleMin,
                attributes: OptionAttributes(
                    "Timing",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number));

        builder.AddOutputPort(
            SessionsComponentDefinition.Ports.Output,
            displayName: Ports.Output,
            group: "Messages",
            order: 0,
            summary: "Replayed record or normal replay failure result.",
            valueType: nameof(SessionContentRecord),
            isPrimary: true);

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateQueryMetadata()
    {
        var builder = CreateSessionMetadataBuilder(
            SessionsComponentDefinition.Types.Query,
            "Session Query",
            "Queries sessions and returns matching metadata in one normal result.",
            "history-search",
            "querySessions");

        builder
            .AddOption(
                Options.SessionName,
                OptionValueKind.Text,
                displayName: "Session Name",
                helperText: "Default exact session name filter.",
                attributes: OptionAttributes(
                    "Filtering",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.NamePrefix,
                OptionValueKind.Text,
                displayName: "Name Prefix",
                helperText: "Default session name prefix filter.",
                attributes: OptionAttributes(
                    "Filtering",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Text))
            .AddOption(TagsOption("Filtering"))
            .AddOption(
                Options.IncludeActive,
                OptionValueKind.Boolean,
                displayName: "Include Active",
                helperText: "Include active sessions in query results.",
                defaultValue: QueryDefaults.IncludeActive,
                attributes: OptionAttributes(
                    "Filtering",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.IncludeCompleted,
                OptionValueKind.Boolean,
                displayName: "Include Completed",
                helperText: "Include completed sessions in query results.",
                defaultValue: QueryDefaults.IncludeCompleted,
                attributes: OptionAttributes(
                    "Filtering",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.Limit,
                OptionValueKind.Number,
                displayName: "Limit",
                helperText: "Maximum number of sessions to return.",
                defaultValue: QueryDefaults.Limit,
                min: 1,
                attributes: OptionAttributes(
                    "Results",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.EmitSessionsInResult,
                OptionValueKind.Boolean,
                displayName: "Emit Sessions In Result",
                helperText: "Include matching session metadata in the query result payload.",
                defaultValue: QueryDefaults.EmitSessionsInResult,
                attributes: OptionAttributes(
                    "Results",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(BoundedCapacityOption(QueryDefaults.BoundedCapacity));

        builder
            .AddInputPort(
                SessionsComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: "Session query request.",
                valueType: nameof(SessionQueryRequest),
                isPrimary: true)
            .AddOutputPort(
                SessionsComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: "Completed or failed session query result.",
                valueType: nameof(SessionQueryOutcome),
                isPrimary: true);

        return builder.Build();
    }

    private static ComponentDesignMetadataBuilder CreateSessionMetadataBuilder(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName)
        => new ComponentDesignMetadataBuilder(type)
            .WithDisplay(
                displayName: displayName,
                category: "Sessions",
                summary: summary,
                iconKey: iconKey,
                preferredNodeName: preferredNodeName,
                suggestedEditorWidth: 460)
            .AddResource(
                SessionsComponentDefinition.Resources.Store,
                displayName: "Store",
                order: 0,
                summary: "Required keyed session store or store factory used to record, replay, or query sessions.",
                valueType: $"{nameof(ISessionStore)} or {nameof(ISessionStoreFactory)}",
                isRequired: true,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Store,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                SessionsComponentDefinition.Resources.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic session timestamps, replay pacing, and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"));

    private static OptionDesignMetadata SessionIdOption(bool isRequired) => new()
    {
        Name = new ComponentOptionName(Options.SessionId),
        Kind = OptionValueKind.Text,
        DisplayName = new ComponentMetadataText("Session ID"),
        HelperText = new ComponentMetadataText(isRequired
            ? "Required session identifier to replay."
            : "Optional session identifier. The store may generate one when omitted."),
        IsRequired = isRequired,
        Attributes = OptionAttributeMap(
            "Session",
            isRequired
                ? OptionDesignMetadataAttributeValues.Primary
                : OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text)
    };

    private static OptionDesignMetadata TagsOption(string section) => new()
    {
        Name = new ComponentOptionName(Options.Tags),
        Kind = OptionValueKind.Json,
        DisplayName = new ComponentMetadataText("Tags"),
        HelperText = new ComponentMetadataText("Optional string tag map used in session metadata or query defaults."),
        Attributes = OptionAttributeMap(
            section,
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Json)
    };

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue)
        => OptionDesignMetadataFactory.BoundedCapacity(
            defaultValue,
            "Maximum queued messages.");

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> OptionAttributeMap(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.CreateMap(
            section: section,
            importance: importance,
            editor: editor);

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);

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
            Value = new ComponentOptionChoiceValue(mode.ToString()),
            DisplayName = new ComponentMetadataText(displayName),
            HelperText = new ComponentMetadataText(helperText)
        };

    private static void AddTransformPorts(
        ComponentDesignMetadataBuilder builder,
        string inputType,
        string inputSummary,
        string outputType,
        string outputSummary)
        => builder
            .AddInputPort(
                SessionsComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: inputSummary,
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                SessionsComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: outputSummary,
                valueType: outputType,
                isPrimary: true);


    public static class Options
    {
        public const string SessionId = "sessionId";
        public const string SessionName = "sessionName";
        public const string Notes = "notes";
        public const string Tags = "tags";
        public const string BoundedCapacity = "boundedCapacity";
        public const string Mode = "mode";
        public const string StartSequence = "startSequence";
        public const string MaxMessages = "maxMessages";
        public const string FixedIntervalMilliseconds = "fixedIntervalMilliseconds";
        public const string SpeedMultiplier = "speedMultiplier";
        public const string NamePrefix = "namePrefix";
        public const string IncludeActive = "includeActive";
        public const string IncludeCompleted = "includeCompleted";
        public const string Limit = "limit";
        public const string EmitSessionsInResult = "emitSessionsInResult";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Recorder =>
            [
                ComponentOptions.Metadata<string>(Options.SessionId),
                ComponentOptions.Metadata<string>(Options.SessionName),
                ComponentOptions.Metadata<string>(Options.Notes),
                ComponentOptions.Metadata<Dictionary<string, string>>(Options.Tags),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Replay =>
            [
                ComponentOptions.Metadata<string>(Options.SessionId, isRequired: true),
                ComponentOptions.Metadata<SessionReplayMode>(Options.Mode),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<long?>(Options.StartSequence),
                ComponentOptions.Metadata<int?>(Options.MaxMessages),
                ComponentOptions.Metadata<double>(Options.FixedIntervalMilliseconds),
                ComponentOptions.Metadata<double>(Options.SpeedMultiplier)
            ],
            Types.Query =>
            [
                ComponentOptions.Metadata<string>(Options.SessionName),
                ComponentOptions.Metadata<string>(Options.NamePrefix),
                ComponentOptions.Metadata<Dictionary<string, string>>(Options.Tags),
                ComponentOptions.Metadata<bool>(Options.IncludeActive),
                ComponentOptions.Metadata<bool>(Options.IncludeCompleted),
                ComponentOptions.Metadata<int>(Options.Limit),
                ComponentOptions.Metadata<bool>(Options.EmitSessionsInResult),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Recorder =>
            [
                ComponentResources.Metadata<ISessionStore>(Resources.Store, isRequired: true, valueTypeHint: "ISessionStore or ISessionStoreFactory"),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Replay =>
            [
                ComponentResources.Metadata<ISessionStore>(Resources.Store, isRequired: true, valueTypeHint: "ISessionStore or ISessionStoreFactory"),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Query =>
            [
                ComponentResources.Metadata<ISessionStore>(Resources.Store, isRequired: true, valueTypeHint: "ISessionStore or ISessionStoreFactory"),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Recorder = "session.record";
        public const string Replay = "session.replay";
        public const string Query = "session.query";
    }

    public static class Ports
    {
        public const string Input = "Input";
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Store = "store";
        public const string Clock = "clock";
    }
}
