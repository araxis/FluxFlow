using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Sessions.Composition;

internal static class SessionsCompositionStoreResolver
{
    public static async ValueTask<ResolvedSessionStore> ResolveAsync(
        ComponentActivationContext context,
        string key,
        string? sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var store = context.Services.GetKeyedService<ISessionStore>(key);
        if (store is not null)
            return ResolvedSessionStore.Shared(store);

        var factory = context.Services.GetKeyedService<ISessionStoreFactory>(key);
        if (factory is null)
        {
            throw new InvalidOperationException(
                $"Component '{context.WorkflowName}.{context.ComponentName}' resource " +
                $"'{SessionsComponentDefinition.Resources.Store}' references '{key}', but no keyed " +
                $"{nameof(ISessionStore)} or {nameof(ISessionStoreFactory)} service is registered.");
        }

        var clock = context.GetResource<TimeProvider>(SessionsComponentDefinition.Resources.Clock);
        var lease = await factory.OpenAsync(new SessionStoreContext
        {
            StoreName = key,
            SessionId = sessionId,
            Clock = clock ?? TimeProvider.System
        }).ConfigureAwait(false);
        return ResolvedSessionStore.Leased(lease);
    }
}

internal sealed class ResolvedSessionStore
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
