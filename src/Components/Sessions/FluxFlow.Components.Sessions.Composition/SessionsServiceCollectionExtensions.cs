using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Sessions.Composition;

public static class SessionsServiceCollectionExtensions
{
    public static IServiceCollection AddSessions(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(SessionsComponentDefinition.Types.Recorder, SessionsServiceCollectionExtensions.CreateSessionRecorderNode)
            .AddComponent(SessionsComponentDefinition.Types.Replay, SessionsServiceCollectionExtensions.CreateSessionReplayNode)
            .AddComponent(SessionsComponentDefinition.Types.Query, SessionsServiceCollectionExtensions.CreateSessionQueryNode);
    }

    internal static async ValueTask<IFlowNode> CreateSessionRecorderNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SessionRecorderOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.SessionId).ConfigureAwait(false);
        try
        {
            var node = new SessionRecorderNode(options, store.Store, clock);

            context.SetLifetime(disposeAsync: store.DisposeAsync);
            context.HasInput(node.Input);
            context.HasOutput(node.Output);
            context.HasEvents(node.Events);
            return node;
        }
        catch
        {
            await store.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async ValueTask<IFlowNode> CreateSessionReplayNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SessionReplayOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.SessionId).ConfigureAwait(false);
        try
        {
            var node = new SessionReplayNode(options, store.Store, clock);

            context.SetLifetime(disposeAsync: store.DisposeAsync);
            context.HasOutput(node.Output);
            context.HasEvents(node.Events);
            return node;
        }
        catch
        {
            await store.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async ValueTask<IFlowNode> CreateSessionQueryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SessionQueryOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, sessionId: null).ConfigureAwait(false);
        try
        {
            var node = new SessionQueryNode(options, store.Store, clock);

            context.SetLifetime(disposeAsync: store.DisposeAsync);
            context.HasInput(node.Input);
            context.HasOutput(node.Output);
            context.HasEvents(node.Events);
            return node;
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
