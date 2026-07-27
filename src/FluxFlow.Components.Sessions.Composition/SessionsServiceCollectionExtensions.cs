using FluxFlow.Components.Designer;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Sessions.Composition;

public static class SessionsServiceCollectionExtensions
{
    internal static ComponentDescriptor RecorderDescriptor { get; } = new(
        SessionsComponentTypes.Recorder,
        CreateSessionRecorderNode,
        inputs:
        [
            ComponentPorts.Metadata<SessionContentRecordInput>(SessionsComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<SessionContentRecord>(SessionsComponentPortNames.Output)
        ]);

    internal static ComponentDescriptor ReplayDescriptor { get; } = new(
        SessionsComponentTypes.Replay,
        CreateSessionReplayNode,
        outputs:
        [
            ComponentPorts.Metadata<SessionContentRecord>(SessionsComponentPortNames.Output)
        ]);

    internal static ComponentDescriptor QueryDescriptor { get; } = new(
        SessionsComponentTypes.Query,
        CreateSessionQueryNode,
        inputs:
        [
            ComponentPorts.Metadata<SessionQueryRequest>(SessionsComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<SessionQueryOutcome>(SessionsComponentPortNames.Output)
        ]);

    public static IServiceCollection AddSessionsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(RecorderDescriptor);
        services.AddFluxFlowComponent(ReplayDescriptor);
        services.AddFluxFlowComponent(QueryDescriptor);
        services.AddComponentDesignMetadataProvider<SessionsComponentDesignMetadataProvider>();
        return services;
    }

    private static async ValueTask<ComponentInstance> CreateSessionRecorderNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SessionRecorderOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsComponentResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.SessionId).ConfigureAwait(false);
        try
        {
            var node = new SessionRecorderNode(options, store.Store, clock);

            return ComponentInstance.Create(
                node,
                inputs:
                [
                    ComponentPorts.Input<SessionContentRecordInput>(
                        SessionsComponentPortNames.Input,
                        node.Input)
                ],
                outputs:
                [
                    ComponentPorts.Output<SessionContentRecord>(
                        SessionsComponentPortNames.Output,
                        node.Output)
                ],
                events: node.Events,
                disposeAsync: store.DisposeAsync);
        }
        catch
        {
            await store.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask<ComponentInstance> CreateSessionReplayNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SessionReplayOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsComponentResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.SessionId).ConfigureAwait(false);
        try
        {
            var node = new SessionReplayNode(options, store.Store, clock);

            return ComponentInstance.Create(
                node,
                outputs:
                [
                    ComponentPorts.Output<SessionContentRecord>(
                        SessionsComponentPortNames.Output,
                        node.Output)
                ],
                events: node.Events,
                disposeAsync: store.DisposeAsync);
        }
        catch
        {
            await store.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask<ComponentInstance> CreateSessionQueryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SessionQueryOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsComponentResourceNames.Clock);
        var store = await ResolveStoreAsync(context, sessionId: null).ConfigureAwait(false);
        try
        {
            var node = new SessionQueryNode(options, store.Store, clock);

            return ComponentInstance.Create(
                node,
                inputs:
                [
                    ComponentPorts.Input<SessionQueryRequest>(
                        SessionsComponentPortNames.Input,
                        node.Input)
                ],
                outputs:
                [
                    ComponentPorts.Output<SessionQueryOutcome>(
                        SessionsComponentPortNames.Output,
                        node.Output)
                ],
                events: node.Events,
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
        var key = context.GetRequiredResourceKey(SessionsComponentResourceNames.Store);
        return await SessionsCompositionStoreResolver.ResolveAsync(context, key, sessionId)
            .ConfigureAwait(false);
    }
}
