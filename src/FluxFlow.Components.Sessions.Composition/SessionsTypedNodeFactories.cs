using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Sessions.Composition;

internal static class SessionsTypedNodeFactories
{
    internal static async ValueTask<ComposedNode> CreateRecorder(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SessionRecorderOptions>();
        var clock = context.GetResource<TimeProvider>(SessionsCompositionResourceNames.Clock);
        var store = await ResolveAsync(context, options.SessionId).ConfigureAwait(false);
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

    internal static async ValueTask<ComposedNode> CreateReplay(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SessionReplayOptions>();
        var clock = context.GetResource<TimeProvider>(SessionsCompositionResourceNames.Clock);
        var store = await ResolveAsync(context, options.SessionId).ConfigureAwait(false);
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

    internal static async ValueTask<ComposedNode> CreateQuery(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SessionQueryOptions>();
        var clock = context.GetResource<TimeProvider>(SessionsCompositionResourceNames.Clock);
        var store = await ResolveAsync(context, sessionId: null).ConfigureAwait(false);
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

    private static ValueTask<ResolvedSessionStore> ResolveAsync(
        CompositionNodeFactoryContext context,
        string? sessionId)
    {
        var key = context.GetRequiredResourceKey(SessionsCompositionResourceNames.Store);
        return SessionsCompositionStoreResolver.ResolveAsync(context, key, sessionId);
    }
}
