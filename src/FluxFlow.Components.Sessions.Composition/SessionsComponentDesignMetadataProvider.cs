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
            SessionsCompositionNodeTypes.Recorder,
            "Session Recorder",
            "Records incoming messages to a host-owned session store.",
            "history",
            "recordSession");

        builder
            .AddOption(StoreOption())
            .AddOption(SessionIdOption(isRequired: false))
            .AddOption(
                "name",
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Optional session name stored with session metadata.")
            .AddOption(
                "notes",
                OptionValueKind.MultilineText,
                displayName: "Notes",
                helperText: "Optional session notes stored with session metadata.")
            .AddOption(TagsOption())
            .AddOption(BoundedCapacityOption(RecorderDefaults.BoundedCapacity));

        AddTransformPorts(
            builder,
            nameof(SessionRecordInput),
            "Session record input.",
            nameof(SessionRecord),
            "Stored session record.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateReplayMetadata()
    {
        var builder = CreateSessionMetadataBuilder(
            SessionsCompositionNodeTypes.Replay,
            "Session Replay",
            "Replays records from a host-owned session store as source messages.",
            "history-play",
            "replaySession");

        builder
            .AddOption(StoreOption())
            .AddOption(SessionIdOption(isRequired: true))
            .AddOption(
                "mode",
                OptionValueKind.Enum,
                displayName: "Mode",
                helperText: "Timing mode used between replayed records.",
                defaultValue: ReplayDefaults.Mode.ToString(),
                choices: ReplayModeChoices())
            .AddOption(BoundedCapacityOption(ReplayDefaults.BoundedCapacity))
            .AddOption(
                "startSequence",
                OptionValueKind.Number,
                displayName: "Start Sequence",
                helperText: "Optional first record sequence to replay.",
                min: 1)
            .AddOption(
                "maxMessages",
                OptionValueKind.Number,
                displayName: "Max Messages",
                helperText: "Optional maximum number of messages to replay.",
                min: 1)
            .AddOption(
                "fixedIntervalMilliseconds",
                OptionValueKind.Number,
                displayName: "Fixed Interval Milliseconds",
                helperText: "Delay used by FixedInterval replay mode.",
                defaultValue: ReplayDefaults.FixedIntervalMilliseconds,
                min: 0)
            .AddOption(
                "speedMultiplier",
                OptionValueKind.Number,
                displayName: "Speed Multiplier",
                helperText: "Multiplier used by Multiplier replay mode; must be greater than zero.",
                defaultValue: ReplayDefaults.SpeedMultiplier,
                min: PositiveDoubleMin);

        builder.AddOutputPort(
            SessionsCompositionPortNames.Output,
            displayName: "Output",
            group: "Messages",
            order: 0,
            summary: "Replayed session record.",
            valueType: nameof(SessionRecord),
            isPrimary: true);

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateQueryMetadata()
    {
        var builder = CreateSessionMetadataBuilder(
            SessionsCompositionNodeTypes.Query,
            "Session Query",
            "Queries sessions and can fan matching sessions to a separate output.",
            "history-search",
            "querySessions");

        builder
            .AddOption(StoreOption())
            .AddOption(
                "name",
                OptionValueKind.Text,
                displayName: "Name",
                helperText: "Default exact session name filter.")
            .AddOption(
                "namePrefix",
                OptionValueKind.Text,
                displayName: "Name Prefix",
                helperText: "Default session name prefix filter.")
            .AddOption(TagsOption())
            .AddOption(
                "includeActive",
                OptionValueKind.Boolean,
                displayName: "Include Active",
                helperText: "Include active sessions in query results.",
                defaultValue: QueryDefaults.IncludeActive)
            .AddOption(
                "includeCompleted",
                OptionValueKind.Boolean,
                displayName: "Include Completed",
                helperText: "Include completed sessions in query results.",
                defaultValue: QueryDefaults.IncludeCompleted)
            .AddOption(
                "limit",
                OptionValueKind.Number,
                displayName: "Limit",
                helperText: "Maximum number of sessions to return.",
                defaultValue: QueryDefaults.Limit,
                min: 1)
            .AddOption(
                "emitSessionsInResult",
                OptionValueKind.Boolean,
                displayName: "Emit Sessions In Result",
                helperText: "Include matching session metadata in the query result payload.",
                defaultValue: QueryDefaults.EmitSessionsInResult)
            .AddOption(
                "emitSessionOutputs",
                OptionValueKind.Boolean,
                displayName: "Emit Session Outputs",
                helperText: "Fan each matching session to the Sessions output.",
                defaultValue: QueryDefaults.EmitSessionOutputs)
            .AddOption(BoundedCapacityOption(QueryDefaults.BoundedCapacity));

        builder
            .AddInputPort(
                SessionsCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Session query request.",
                valueType: nameof(SessionQueryRequest),
                isPrimary: true)
            .AddOutputPort(
                SessionsCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Session query result.",
                valueType: nameof(SessionQueryResult),
                isPrimary: true)
            .AddOutputPort(
                SessionsCompositionPortNames.Sessions,
                displayName: "Sessions",
                group: "Sessions",
                order: 2,
                summary: "Matching session metadata.",
                valueType: nameof(SessionMetadata));

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
                SessionsCompositionResourceNames.Store,
                displayName: "Store",
                order: 0,
                summary: "Required keyed session store or store factory used to record, replay, or query sessions.",
                valueType: $"{nameof(ISessionStore)} or {nameof(ISessionStoreFactory)}",
                isRequired: true,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Store))
            .AddResource(
                SessionsCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic session timestamps, replay pacing, and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock));

    private static OptionDesignMetadata StoreOption() => new()
    {
        Name = new ComponentOptionName("store"),
        Kind = OptionValueKind.Text,
        DisplayName = new ComponentMetadataText("Store"),
        HelperText = new ComponentMetadataText("Diagnostic store metadata; DI selection uses the required host-owned store resource.")
    };

    private static OptionDesignMetadata SessionIdOption(bool isRequired) => new()
    {
        Name = new ComponentOptionName("sessionId"),
        Kind = OptionValueKind.Text,
        DisplayName = new ComponentMetadataText("Session ID"),
        HelperText = new ComponentMetadataText(isRequired
            ? "Required session identifier to replay."
            : "Optional session identifier. The store may generate one when omitted."),
        IsRequired = isRequired
    };

    private static OptionDesignMetadata TagsOption() => new()
    {
        Name = new ComponentOptionName("tags"),
        Kind = OptionValueKind.Json,
        DisplayName = new ComponentMetadataText("Tags"),
        HelperText = new ComponentMetadataText("Optional string tag map used in session metadata or query defaults.")
    };

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue) => new()
    {
        Name = new ComponentOptionName("boundedCapacity"),
        Kind = OptionValueKind.Number,
        DisplayName = new ComponentMetadataText("Bounded Capacity"),
        DefaultValue = defaultValue,
        Min = 1,
        HelperText = new ComponentMetadataText("Maximum queued messages.")
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
                SessionsCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: inputSummary,
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                SessionsCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: outputSummary,
                valueType: outputType,
                isPrimary: true);
}
