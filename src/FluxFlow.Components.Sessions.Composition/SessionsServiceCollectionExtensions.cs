using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Sessions.Composition;

public static class SessionsServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddSessions(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignedComponent(SessionsComponents.SessionRecorder)
            .AddDesignedComponent(SessionsComponents.SessionReplay)
            .AddDesignedComponent(SessionsComponents.SessionQuery);
    }

    internal static void ConfigureRecorder(ComponentRegistrationBuilder component)
    {
        var defaults = new SessionRecorderOptions();
        ConfigureCommon(component, "Session Recorder", "Records incoming messages to a host-owned session store.", "history", "recordSession");
        AddSessionId(component, false);
        component.AddOption<string>(SessionsComponentDefinition.Options.SessionName, OptionValueKind.Text, "Session Name", "Optional session name stored with session metadata.", section: "Session", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(SessionsComponentDefinition.Options.Notes, OptionValueKind.MultilineText, "Notes", "Optional session notes stored with session metadata.", section: "Session", importance: OptionDesignMetadataAttributeValues.Advanced);
        AddTags(component, "Metadata");
        AddCapacity(component, defaults.BoundedCapacity);
        component
            .UseFactory(CreateSessionRecorderNode)
            .HasInput(SessionsComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Exact-content session record input.", true)
            .HasOutput(SessionsComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Stored or failed session record result.", true)
            .HasEvents(SessionsComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort session-recorder diagnostics.");
    }

    internal static void ConfigureReplay(ComponentRegistrationBuilder component)
    {
        var defaults = new SessionReplayOptions();
        ConfigureCommon(component, "Session Replay", "Replays records from a host-owned session store as source messages.", "history-play", "replaySession");
        AddSessionId(component, true);
        component.AddOption<SessionReplayMode>(SessionsComponentDefinition.Options.Mode, OptionValueKind.Enum, "Mode", "Timing mode used between replayed records.", defaultValue: defaults.Mode.ToString(), section: "Replay", importance: OptionDesignMetadataAttributeValues.Primary);
        component.AddOptionChoice(SessionsComponentDefinition.Options.Mode, SessionReplayMode.RealTime.ToString(), "Real Time", "Use timestamp deltas from stored records.");
        component.AddOptionChoice(SessionsComponentDefinition.Options.Mode, SessionReplayMode.FixedInterval.ToString(), "Fixed Interval", "Use a fixed delay between records.");
        component.AddOptionChoice(SessionsComponentDefinition.Options.Mode, SessionReplayMode.Multiplier.ToString(), "Multiplier", "Use timestamp deltas divided by speed multiplier.");
        component.AddOptionChoice(SessionsComponentDefinition.Options.Mode, SessionReplayMode.Instant.ToString(), "Instant", "Emit records without inter-record delay.");
        AddCapacity(component, defaults.BoundedCapacity);
        component.AddOption<long?>(SessionsComponentDefinition.Options.StartSequence, OptionValueKind.Number, "Start Sequence", "Optional first record sequence to replay.", min: 1, section: "Replay", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int?>(SessionsComponentDefinition.Options.MaxMessages, OptionValueKind.Number, "Max Messages", "Optional maximum number of messages to replay.", min: 1, section: "Replay", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<double>(SessionsComponentDefinition.Options.FixedIntervalMilliseconds, OptionValueKind.Number, "Fixed Interval Milliseconds", "Delay used by FixedInterval replay mode.", defaultValue: defaults.FixedIntervalMilliseconds, min: 0, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<double>(SessionsComponentDefinition.Options.SpeedMultiplier, OptionValueKind.Number, "Speed Multiplier", "Multiplier used by Multiplier replay mode; must be greater than zero.", defaultValue: defaults.SpeedMultiplier, min: 0.000001, section: "Timing", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component
            .UseFactory(CreateSessionReplayNode)
            .HasOutput(SessionsComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 0, "Replayed record or normal replay failure result.", true)
            .HasEvents(SessionsComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 1, "Best-effort session-replay diagnostics.");
    }

    internal static void ConfigureQuery(ComponentRegistrationBuilder component)
    {
        var defaults = new SessionQueryOptions();
        ConfigureCommon(component, "Session Query", "Queries sessions and returns matching metadata in one normal result.", "history-search", "querySessions");
        component.AddOption<string>(SessionsComponentDefinition.Options.SessionName, OptionValueKind.Text, "Session Name", "Default exact session name filter.", section: "Filtering", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(SessionsComponentDefinition.Options.NamePrefix, OptionValueKind.Text, "Name Prefix", "Default session name prefix filter.", section: "Filtering", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        AddTags(component, "Filtering");
        component.AddOption<bool>(SessionsComponentDefinition.Options.IncludeActive, OptionValueKind.Boolean, "Include Active", "Include active sessions in query results.", defaultValue: defaults.IncludeActive, section: "Filtering", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<bool>(SessionsComponentDefinition.Options.IncludeCompleted, OptionValueKind.Boolean, "Include Completed", "Include completed sessions in query results.", defaultValue: defaults.IncludeCompleted, section: "Filtering", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<int>(SessionsComponentDefinition.Options.Limit, OptionValueKind.Number, "Limit", "Maximum number of sessions to return.", defaultValue: defaults.Limit, min: 1, section: "Results", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<bool>(SessionsComponentDefinition.Options.EmitSessionsInResult, OptionValueKind.Boolean, "Emit Sessions In Result", "Include matching session metadata in the query result payload.", defaultValue: defaults.EmitSessionsInResult, section: "Results", importance: OptionDesignMetadataAttributeValues.Advanced);
        AddCapacity(component, defaults.BoundedCapacity);
        component
            .UseFactory(CreateSessionQueryNode)
            .HasInput(SessionsComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Session query request.", true)
            .HasOutput(SessionsComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Completed or failed session query result.", true)
            .HasEvents(SessionsComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort session-query diagnostics.");
    }

    private static void ConfigureCommon(ComponentRegistrationBuilder component, string displayName, string summary, string iconKey, string preferredNodeName)
    {
        component.WithDisplay(displayName, "Sessions", summary, iconKey, preferredNodeName, 460);
        component.AddResource<ISessionStore>(SessionsComponentDefinition.Resources.Store, "Store", 0, "Required keyed session store or store factory used to record, replay, or query sessions.", true, "ISessionStore or ISessionStoreFactory", "ISessionStore or ISessionStoreFactory", ResourceDesignMetadataAttributeValues.HostOwned, ResourceDesignMetadataAttributeValues.Store, "Resources.{name}");
        component.AddResource<TimeProvider>(SessionsComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic session timestamps, replay pacing, and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
    }

    private static void AddSessionId(ComponentRegistrationBuilder component, bool required)
        => component.AddOption<string>(SessionsComponentDefinition.Options.SessionId, OptionValueKind.Text, "Session ID", required ? "Required session identifier to replay." : "Optional session identifier. The store may generate one when omitted.", required, section: "Session", importance: required ? OptionDesignMetadataAttributeValues.Primary : OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);

    private static void AddTags(ComponentRegistrationBuilder component, string section)
        => component.AddOption<Dictionary<string, string>>(SessionsComponentDefinition.Options.Tags, OptionValueKind.Json, "Tags", "Optional string tag map used in session metadata or query defaults.", section: section, importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Json);

    private static void AddCapacity(ComponentRegistrationBuilder component, int defaultValue)
        => component.AddOption<int>(SessionsComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded component work and reliable normal-data output.", defaultValue: defaultValue, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static async ValueTask<ComponentNodeActivation<SessionRecorderNode>> CreateSessionRecorderNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SessionRecorderOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.SessionId).ConfigureAwait(false);
        try
        {
            var node = new SessionRecorderNode(options, store.Store, clock);

            return new ComponentNodeActivation<SessionRecorderNode>(
                node,
                disposeAsync: store.DisposeAsync);
        }
        catch
        {
            await store.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask<ComponentNodeActivation<SessionReplayNode>> CreateSessionReplayNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SessionReplayOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.SessionId).ConfigureAwait(false);
        try
        {
            var node = new SessionReplayNode(options, store.Store, clock);

            return new ComponentNodeActivation<SessionReplayNode>(
                node,
                disposeAsync: store.DisposeAsync);
        }
        catch
        {
            await store.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask<ComponentNodeActivation<SessionQueryNode>> CreateSessionQueryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SessionQueryOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, sessionId: null).ConfigureAwait(false);
        try
        {
            var node = new SessionQueryNode(options, store.Store, clock);

            return new ComponentNodeActivation<SessionQueryNode>(
                node,
                disposeAsync: store.DisposeAsync);
        }
        catch
        {
            await store.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask<ResolvedSessionStore> ResolveStoreAsync(
        ComponentActivationContext context,
        string? sessionId)
    {
        var key = context.GetRequiredResourceKey(SessionsComponentDefinition.Resources.Store);
        return await SessionsCompositionStoreResolver.ResolveAsync(context, key, sessionId)
            .ConfigureAwait(false);
    }
}
