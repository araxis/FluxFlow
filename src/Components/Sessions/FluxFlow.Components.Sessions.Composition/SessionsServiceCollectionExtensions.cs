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
            .AddComponent(SessionsComponents.SessionRecorder)
            .AddComponent(SessionsComponents.SessionReplay)
            .AddComponent(SessionsComponents.SessionQuery);
    }

    internal static void ConfigureRecorder(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<ISessionStore>(SessionsComponentDefinition.Resources.Store, isRequired: true, valueTypeHint: "ISessionStore or ISessionStoreFactory");
        component.AddResource<TimeProvider>(SessionsComponentDefinition.Resources.Clock);
        component.AddOption<string>(SessionsComponentDefinition.Options.SessionId);
        component.AddOption<string>(SessionsComponentDefinition.Options.SessionName);
        component.AddOption<string>(SessionsComponentDefinition.Options.Notes);
        component.AddOption<Dictionary<string, string>>(SessionsComponentDefinition.Options.Tags);
        component.AddOption<int>(SessionsComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateSessionRecorderNode)
            .HasStandardPorts(new FluxFlow.Components.Sessions.Materialization.SessionContentRecordInputMaterializer().InputShape);
    }

    internal static void ConfigureReplay(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<ISessionStore>(SessionsComponentDefinition.Resources.Store, isRequired: true, valueTypeHint: "ISessionStore or ISessionStoreFactory");
        component.AddResource<TimeProvider>(SessionsComponentDefinition.Resources.Clock);
        component.AddOption<string>(SessionsComponentDefinition.Options.SessionId, isRequired: true);
        component.AddOption<SessionReplayMode>(SessionsComponentDefinition.Options.Mode);
        component.AddOption<int>(SessionsComponentDefinition.Options.BoundedCapacity);
        component.AddOption<long?>(SessionsComponentDefinition.Options.StartSequence);
        component.AddOption<int?>(SessionsComponentDefinition.Options.MaxMessages);
        component.AddOption<double>(SessionsComponentDefinition.Options.FixedIntervalMilliseconds);
        component.AddOption<double>(SessionsComponentDefinition.Options.SpeedMultiplier);
        component
            .UseFactory(CreateSessionReplayNode)
            .HasOutput(SessionsComponentDefinition.Ports.Output, static node => node.Output)
            .HasEvents(SessionsComponentDefinition.Ports.Events, static node => node.Events);
    }

    internal static void ConfigureQuery(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<ISessionStore>(SessionsComponentDefinition.Resources.Store, isRequired: true, valueTypeHint: "ISessionStore or ISessionStoreFactory");
        component.AddResource<TimeProvider>(SessionsComponentDefinition.Resources.Clock);
        component.AddOption<string>(SessionsComponentDefinition.Options.SessionName);
        component.AddOption<string>(SessionsComponentDefinition.Options.NamePrefix);
        component.AddOption<Dictionary<string, string>>(SessionsComponentDefinition.Options.Tags);
        component.AddOption<bool>(SessionsComponentDefinition.Options.IncludeActive);
        component.AddOption<bool>(SessionsComponentDefinition.Options.IncludeCompleted);
        component.AddOption<int>(SessionsComponentDefinition.Options.Limit);
        component.AddOption<bool>(SessionsComponentDefinition.Options.EmitSessionsInResult);
        component.AddOption<int>(SessionsComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateSessionQueryNode)
            .HasStandardPorts(new FluxFlow.Components.Sessions.Materialization.SessionQueryRequestMaterializer().InputShape);
    }

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
