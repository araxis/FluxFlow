using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Sessions.Composition;

public static class SessionsAuthoringExtensions
{
    public static InputOutputComponentHandle<SessionContentRecordInput, SessionContentRecord> AddSessionRecorder(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SessionRecorderComponentBuilder> configure)
        => Add<SessionContentRecordInput, SessionContentRecord, SessionRecorderComponentBuilder>(
            workflow, name, SessionsComponentDefinition.Types.Recorder, configure, static (builder, definition) => builder.Apply(definition));

    public static WorkflowDefinitionBuilder AddSessionRecorder(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SessionRecorderComponentBuilder> configure,
        out InputOutputComponentHandle<SessionContentRecordInput, SessionContentRecord> recorder)
    {
        recorder = workflow.AddSessionRecorder(name, configure);
        return workflow;
    }

    public static OutputComponentHandle<SessionContentRecord> AddSessionReplay(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SessionReplayComponentBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var component = workflow.AddComponent(name, SessionsComponentDefinition.Types.Replay, definition =>
        {
            var builder = new SessionReplayComponentBuilder();
            configure(builder);
            builder.Apply(definition);
        });
        return new(component, SessionsComponentDefinition.Ports.Output);
    }

    public static WorkflowDefinitionBuilder AddSessionReplay(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SessionReplayComponentBuilder> configure,
        out OutputComponentHandle<SessionContentRecord> replay)
    {
        replay = workflow.AddSessionReplay(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<SessionQueryRequest, SessionQueryOutcome> AddSessionQuery(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SessionQueryComponentBuilder> configure)
        => Add<SessionQueryRequest, SessionQueryOutcome, SessionQueryComponentBuilder>(
            workflow, name, SessionsComponentDefinition.Types.Query, configure, static (builder, definition) => builder.Apply(definition));

    public static WorkflowDefinitionBuilder AddSessionQuery(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SessionQueryComponentBuilder> configure,
        out InputOutputComponentHandle<SessionQueryRequest, SessionQueryOutcome> query)
    {
        query = workflow.AddSessionQuery(name, configure);
        return workflow;
    }

    private static InputOutputComponentHandle<TInput, TOutput> Add<TInput, TOutput, TBuilder>(
        WorkflowDefinitionBuilder workflow,
        string name,
        string type,
        Action<TBuilder> configure,
        Action<TBuilder, ComponentDefinitionBuilder> apply)
        where TBuilder : SessionComponentBuilder, new()
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var component = workflow.AddComponent(name, type, definition =>
        {
            var builder = new TBuilder();
            configure(builder);
            apply(builder, definition);
        });
        return new(component, SessionsComponentDefinition.Ports.Input, SessionsComponentDefinition.Ports.Output);
    }
}

public abstract class SessionComponentBuilder
{
    public int? BoundedCapacity { get; set; }
    public ResourceHandle<ISessionStore>? Store { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    private protected void ApplyCommon(ComponentDefinitionBuilder definition)
    {
        if (Store is null)
            throw new InvalidOperationException("Session components require Store.");
        Set(definition, SessionsComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        definition.UseResource(SessionsComponentDefinition.Resources.Store, Store);
        if (Clock is not null)
            definition.UseResource(SessionsComponentDefinition.Resources.Clock, Clock);
    }

    private protected static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class SessionRecorderComponentBuilder : SessionComponentBuilder
{
    public string? SessionId { get; set; }
    public string? SessionName { get; set; }
    public string? Notes { get; set; }
    public IReadOnlyDictionary<string, string>? Tags { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, SessionsComponentDefinition.Options.SessionId, SessionId);
        Set(definition, SessionsComponentDefinition.Options.SessionName, SessionName);
        Set(definition, SessionsComponentDefinition.Options.Notes, Notes);
        Set(definition, SessionsComponentDefinition.Options.Tags, Tags);
    }
}

public sealed class SessionReplayComponentBuilder : SessionComponentBuilder
{
    public string? SessionId { get; set; }
    public SessionReplayMode? Mode { get; set; }
    public long? StartSequence { get; set; }
    public int? MaxMessages { get; set; }
    public double? FixedIntervalMilliseconds { get; set; }
    public double? SpeedMultiplier { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        if (string.IsNullOrWhiteSpace(SessionId))
            throw new InvalidOperationException("Session replay components require SessionId.");
        definition.Set(SessionsComponentDefinition.Options.SessionId, SessionId);
        Set(definition, SessionsComponentDefinition.Options.Mode, Mode);
        Set(definition, SessionsComponentDefinition.Options.StartSequence, StartSequence);
        Set(definition, SessionsComponentDefinition.Options.MaxMessages, MaxMessages);
        Set(definition, SessionsComponentDefinition.Options.FixedIntervalMilliseconds, FixedIntervalMilliseconds);
        Set(definition, SessionsComponentDefinition.Options.SpeedMultiplier, SpeedMultiplier);
    }
}

public sealed class SessionQueryComponentBuilder : SessionComponentBuilder
{
    public string? SessionName { get; set; }
    public string? NamePrefix { get; set; }
    public IReadOnlyDictionary<string, string>? Tags { get; set; }
    public bool? IncludeActive { get; set; }
    public bool? IncludeCompleted { get; set; }
    public int? Limit { get; set; }
    public bool? EmitSessionsInResult { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, SessionsComponentDefinition.Options.SessionName, SessionName);
        Set(definition, SessionsComponentDefinition.Options.NamePrefix, NamePrefix);
        Set(definition, SessionsComponentDefinition.Options.Tags, Tags);
        Set(definition, SessionsComponentDefinition.Options.IncludeActive, IncludeActive);
        Set(definition, SessionsComponentDefinition.Options.IncludeCompleted, IncludeCompleted);
        Set(definition, SessionsComponentDefinition.Options.Limit, Limit);
        Set(definition, SessionsComponentDefinition.Options.EmitSessionsInResult, EmitSessionsInResult);
    }
}
