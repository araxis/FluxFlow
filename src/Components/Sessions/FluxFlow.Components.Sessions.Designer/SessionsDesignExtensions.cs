using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Sessions.Composition;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Sessions.Designer;

public static class SessionsDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddSessionsDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(SessionsComponentDefinition.Types.Recorder, DescribeRecorder)
            .AddDesignMetadata(SessionsComponentDefinition.Types.Replay, DescribeReplay)
            .AddDesignMetadata(SessionsComponentDefinition.Types.Query, DescribeQuery);
    }

    private static ComponentDesignMetadata DescribeRecorder(ComponentDesignMetadata metadata)
    {
        var defaults = new SessionRecorderOptions();
        metadata = metadata.WithDisplay("Session Recorder", "Sessions", "Records incoming messages to a host-owned session store.", "history", "recordSession", 460);
        metadata = metadata.WithResource(SessionsComponentDefinition.Resources.Store, "Store", 0, "Required keyed session store or store factory used to record, replay, or query sessions.", designValueType: "ISessionStore or ISessionStoreFactory", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Store, keyPattern: "Resources.{name}");
        metadata = metadata.WithResource(SessionsComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic session timestamps, replay pacing, and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.SessionId, OptionValueKind.Text, "Session ID", "Optional session identifier. The store may generate one when omitted.", section: "Session", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.SessionName, OptionValueKind.Text, "Session Name", "Optional session name stored with session metadata.", section: "Session", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.Notes, OptionValueKind.MultilineText, "Notes", "Optional session notes stored with session metadata.", section: "Session", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.Tags, OptionValueKind.Json, "Tags", "Optional string tag map used in session metadata or query defaults.", section: "Metadata", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Json);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded component work and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(SessionsComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Exact-content session record input.", true)
            .WithPort(SessionsComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Stored or failed session record result.", true)
            .WithPort(SessionsComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort session-recorder diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeReplay(ComponentDesignMetadata metadata)
    {
        var defaults = new SessionReplayOptions();
        metadata = metadata.WithDisplay("Session Replay", "Sessions", "Replays records from a host-owned session store as source messages.", "history-play", "replaySession", 460);
        metadata = metadata.WithResource(SessionsComponentDefinition.Resources.Store, "Store", 0, "Required keyed session store or store factory used to record, replay, or query sessions.", designValueType: "ISessionStore or ISessionStoreFactory", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Store, keyPattern: "Resources.{name}");
        metadata = metadata.WithResource(SessionsComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic session timestamps, replay pacing, and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.SessionId, OptionValueKind.Text, "Session ID", "Required session identifier to replay.", section: "Session", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.Mode, OptionValueKind.Enum, "Mode", "Timing mode used between replayed records.", defaultValue: defaults.Mode.ToString(), section: "Replay", importance: OptionDesignMetadataAttributeValues.Primary);
        metadata = metadata.WithOptionChoice(SessionsComponentDefinition.Options.Mode, SessionReplayMode.RealTime.ToString(), "Real Time", "Use timestamp deltas from stored records.");
        metadata = metadata.WithOptionChoice(SessionsComponentDefinition.Options.Mode, SessionReplayMode.FixedInterval.ToString(), "Fixed Interval", "Use a fixed delay between records.");
        metadata = metadata.WithOptionChoice(SessionsComponentDefinition.Options.Mode, SessionReplayMode.Multiplier.ToString(), "Multiplier", "Use timestamp deltas divided by speed multiplier.");
        metadata = metadata.WithOptionChoice(SessionsComponentDefinition.Options.Mode, SessionReplayMode.Instant.ToString(), "Instant", "Emit records without inter-record delay.");
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded component work and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.StartSequence, OptionValueKind.Number, "Start Sequence", "Optional first record sequence to replay.", min: 1, section: "Replay", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.MaxMessages, OptionValueKind.Number, "Max Messages", "Optional maximum number of messages to replay.", min: 1, section: "Replay", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.FixedIntervalMilliseconds, OptionValueKind.Number, "Fixed Interval Milliseconds", "Delay used by FixedInterval replay mode.", defaultValue: defaults.FixedIntervalMilliseconds, min: 0, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.SpeedMultiplier, OptionValueKind.Number, "Speed Multiplier", "Multiplier used by Multiplier replay mode; must be greater than zero.", defaultValue: defaults.SpeedMultiplier, min: 0.000001, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(SessionsComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Messages", 0, "Replayed record or normal replay failure result.", true)
            .WithPort(SessionsComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 1, "Best-effort session-replay diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeQuery(ComponentDesignMetadata metadata)
    {
        var defaults = new SessionQueryOptions();
        metadata = metadata.WithDisplay("Session Query", "Sessions", "Queries sessions and returns matching metadata in one normal result.", "history-search", "querySessions", 460);
        metadata = metadata.WithResource(SessionsComponentDefinition.Resources.Store, "Store", 0, "Required keyed session store or store factory used to record, replay, or query sessions.", designValueType: "ISessionStore or ISessionStoreFactory", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Store, keyPattern: "Resources.{name}");
        metadata = metadata.WithResource(SessionsComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic session timestamps, replay pacing, and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.SessionName, OptionValueKind.Text, "Session Name", "Default exact session name filter.", section: "Filtering", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.NamePrefix, OptionValueKind.Text, "Name Prefix", "Default session name prefix filter.", section: "Filtering", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.Tags, OptionValueKind.Json, "Tags", "Optional string tag map used in session metadata or query defaults.", section: "Filtering", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Json);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.IncludeActive, OptionValueKind.Boolean, "Include Active", "Include active sessions in query results.", defaultValue: defaults.IncludeActive, section: "Filtering", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.IncludeCompleted, OptionValueKind.Boolean, "Include Completed", "Include completed sessions in query results.", defaultValue: defaults.IncludeCompleted, section: "Filtering", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.Limit, OptionValueKind.Number, "Limit", "Maximum number of sessions to return.", defaultValue: defaults.Limit, min: 1, section: "Results", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.EmitSessionsInResult, OptionValueKind.Boolean, "Emit Sessions In Result", "Include matching session metadata in the query result payload.", defaultValue: defaults.EmitSessionsInResult, section: "Results", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(SessionsComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded component work and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(SessionsComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Session query request.", true)
            .WithPort(SessionsComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Completed or failed session query result.", true)
            .WithPort(SessionsComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort session-query diagnostics.");
        return metadata;
    }
}
