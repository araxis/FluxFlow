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
    {
        var builder = CreateSessionMetadataBuilder(
            SessionsComponentTypes.Recorder,
            "Session Recorder",
            "Records incoming messages to a host-owned session store.",
            "history",
            "recordSession");

        builder
            .AddOption(SessionIdOption(isRequired: false))
            .AddOption(
                "sessionName",
                OptionValueKind.Text,
                displayName: "Session Name",
                helperText: "Optional session name stored with session metadata.",
                attributes: OptionAttributes(
                    "Session",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "notes",
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
            SessionsComponentTypes.Replay,
            "Session Replay",
            "Replays records from a host-owned session store as source messages.",
            "history-play",
            "replaySession");

        builder
            .AddOption(SessionIdOption(isRequired: true))
            .AddOption(
                "mode",
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
                "startSequence",
                OptionValueKind.Number,
                displayName: "Start Sequence",
                helperText: "Optional first record sequence to replay.",
                min: 1,
                attributes: OptionAttributes(
                    "Replay",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                "maxMessages",
                OptionValueKind.Number,
                displayName: "Max Messages",
                helperText: "Optional maximum number of messages to replay.",
                min: 1,
                attributes: OptionAttributes(
                    "Replay",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                "fixedIntervalMilliseconds",
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
                "speedMultiplier",
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
            SessionsComponentPortNames.Output,
            displayName: "Output",
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
            SessionsComponentTypes.Query,
            "Session Query",
            "Queries sessions and returns matching metadata in one normal result.",
            "history-search",
            "querySessions");

        builder
            .AddOption(
                "sessionName",
                OptionValueKind.Text,
                displayName: "Session Name",
                helperText: "Default exact session name filter.",
                attributes: OptionAttributes(
                    "Filtering",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "namePrefix",
                OptionValueKind.Text,
                displayName: "Name Prefix",
                helperText: "Default session name prefix filter.",
                attributes: OptionAttributes(
                    "Filtering",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Text))
            .AddOption(TagsOption("Filtering"))
            .AddOption(
                "includeActive",
                OptionValueKind.Boolean,
                displayName: "Include Active",
                helperText: "Include active sessions in query results.",
                defaultValue: QueryDefaults.IncludeActive,
                attributes: OptionAttributes(
                    "Filtering",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "includeCompleted",
                OptionValueKind.Boolean,
                displayName: "Include Completed",
                helperText: "Include completed sessions in query results.",
                defaultValue: QueryDefaults.IncludeCompleted,
                attributes: OptionAttributes(
                    "Filtering",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "limit",
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
                "emitSessionsInResult",
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
                SessionsComponentPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Session query request.",
                valueType: nameof(SessionQueryRequest),
                isPrimary: true)
            .AddOutputPort(
                SessionsComponentPortNames.Output,
                displayName: "Output",
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
                SessionsComponentResourceNames.Store,
                displayName: "Store",
                order: 0,
                summary: "Required keyed session store or store factory used to record, replay, or query sessions.",
                valueType: $"{nameof(ISessionStore)} or {nameof(ISessionStoreFactory)}",
                isRequired: true,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Store,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                SessionsComponentResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic session timestamps, replay pacing, and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"));

    private static OptionDesignMetadata SessionIdOption(bool isRequired) => new()
    {
        Name = new ComponentOptionName("sessionId"),
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
        Name = new ComponentOptionName("tags"),
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
                SessionsComponentPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: inputSummary,
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                SessionsComponentPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: outputSummary,
                valueType: outputType,
                isPrimary: true);
}
