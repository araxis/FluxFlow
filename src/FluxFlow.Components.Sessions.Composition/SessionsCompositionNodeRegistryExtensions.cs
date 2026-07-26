using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Sessions.Composition;

public static class SessionsCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterSessionRecorder(
        this CompositionNodeRegistry registry,
        string nodeType = SessionsCompositionNodeTypes.Recorder)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            SessionsCompositionNodeTypes.RecorderDescriptor,
            CreateSessionRecorderNode,
            inputs:
            [
                CompositionPorts.Metadata<SessionContentRecordInput>(
                    SessionsCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<SessionContentRecord>(
                    SessionsCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    public static CompositionNodeRegistry RegisterSessionReplay(
        this CompositionNodeRegistry registry,
        string nodeType = SessionsCompositionNodeTypes.Replay)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateSessionReplayNode,
            outputs:
            [
                CompositionPorts.Metadata<SessionContentRecord>(
                    SessionsCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterSessionQuery(
        this CompositionNodeRegistry registry,
        string nodeType = SessionsCompositionNodeTypes.Query)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateSessionQueryNode,
            inputs:
            [
                CompositionPorts.Metadata<SessionQueryRequest>(
                    SessionsCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<SessionQueryOutcome>(
                    SessionsCompositionPortNames.Output)
            ]);
    }

    private static async ValueTask<ComposedNode> CreateSessionRecorderNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SessionRecorderOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsCompositionResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.SessionId).ConfigureAwait(false);
        try
        {
            var node = new SessionRecorderNode(options, store.Store, clock);

            return ComposedNode.Create(
                node,
                inputs:
                [
                    CompositionPorts.Input<SessionContentRecordInput>(
                        SessionsCompositionPortNames.Input,
                        node.Input)
                ],
                outputs:
                [
                    CompositionPorts.Output<SessionContentRecord>(
                        SessionsCompositionPortNames.Output,
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

    private static async ValueTask<ComposedNode> CreateSessionReplayNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SessionReplayOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsCompositionResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.SessionId).ConfigureAwait(false);
        try
        {
            var node = new SessionReplayNode(options, store.Store, clock);

            return ComposedNode.Create(
                node,
                outputs:
                [
                    CompositionPorts.Output<SessionContentRecord>(
                        SessionsCompositionPortNames.Output,
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

    private static async ValueTask<ComposedNode> CreateSessionQueryNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SessionQueryOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsCompositionResourceNames.Clock);
        var store = await ResolveStoreAsync(context, sessionId: null).ConfigureAwait(false);
        try
        {
            var node = new SessionQueryNode(options, store.Store, clock);

            return ComposedNode.Create(
                node,
                inputs:
                [
                    CompositionPorts.Input<SessionQueryRequest>(
                        SessionsCompositionPortNames.Input,
                        node.Input)
                ],
                outputs:
                [
                    CompositionPorts.Output<SessionQueryOutcome>(
                        SessionsCompositionPortNames.Output,
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
        CompositionNodeFactoryContext context,
        string? sessionId)
    {
        var key = context.GetRequiredResourceKey(SessionsCompositionResourceNames.Store);
        return await SessionsCompositionStoreResolver.ResolveAsync(context, key, sessionId)
            .ConfigureAwait(false);
    }
}
