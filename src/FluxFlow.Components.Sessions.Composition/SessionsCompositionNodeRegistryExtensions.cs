using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using Microsoft.Extensions.DependencyInjection;

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
            nodeType,
            CreateSessionRecorderNode,
            inputs:
            [
                CompositionPorts.Metadata<SessionRecordInput>(
                    SessionsCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<SessionRecord>(
                    SessionsCompositionPortNames.Output)
            ]);
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
                CompositionPorts.Metadata<SessionRecord>(
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
                CompositionPorts.Metadata<SessionQueryResult>(
                    SessionsCompositionPortNames.Output),
                CompositionPorts.Metadata<SessionMetadata>(
                    SessionsCompositionPortNames.Sessions)
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
                    CompositionPorts.Input<SessionRecordInput>(
                        SessionsCompositionPortNames.Input,
                        node.Input)
                ],
                outputs:
                [
                    CompositionPorts.Output<SessionRecord>(
                        SessionsCompositionPortNames.Output,
                        node.Output)
                ],
                events: node.Events,
                errors: node.Errors,
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
                    CompositionPorts.Output<SessionRecord>(
                        SessionsCompositionPortNames.Output,
                        node.Output)
                ],
                events: node.Events,
                errors: node.Errors,
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
                    CompositionPorts.Output<SessionQueryResult>(
                        SessionsCompositionPortNames.Output,
                        node.Output),
                    CompositionPorts.Output<SessionMetadata>(
                        SessionsCompositionPortNames.Sessions,
                        node.Sessions)
                ],
                events: node.Events,
                errors: node.Errors,
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
        var store = context.Services.GetKeyedService<ISessionStore>(key);
        if (store is not null)
            return ResolvedSessionStore.Shared(store);

        var factory = context.Services.GetKeyedService<ISessionStoreFactory>(key);
        if (factory is null)
        {
            throw new InvalidOperationException(
                $"Node '{context.WorkflowName}.{context.NodeName}' resource " +
                $"'{SessionsCompositionResourceNames.Store}' references '{key}', but no keyed " +
                $"{nameof(ISessionStore)} or {nameof(ISessionStoreFactory)} service is registered.");
        }

        var clock = context.GetResource<TimeProvider>(SessionsCompositionResourceNames.Clock);
        var lease = await factory
            .OpenAsync(new SessionStoreContext
            {
                StoreName = key,
                SessionId = sessionId,
                Clock = clock ?? TimeProvider.System
            })
            .ConfigureAwait(false);

        return ResolvedSessionStore.Leased(lease);
    }

    private sealed class ResolvedSessionStore
    {
        private readonly SessionStoreLease? _lease;

        private ResolvedSessionStore(ISessionStore store, SessionStoreLease? lease)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            _lease = lease;
        }

        public ISessionStore Store { get; }

        public static ResolvedSessionStore Shared(ISessionStore store)
            => new(store, lease: null);

        public static ResolvedSessionStore Leased(SessionStoreLease lease)
        {
            ArgumentNullException.ThrowIfNull(lease);
            return new ResolvedSessionStore(lease.Store, lease);
        }

        public ValueTask DisposeAsync()
            => _lease?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
