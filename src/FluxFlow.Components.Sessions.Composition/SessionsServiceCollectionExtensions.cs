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
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                RecorderDescriptor,
                ReplayDescriptor,
                QueryDescriptor
            ],
            SessionsComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor RecorderDescriptor { get; } = new(
        SessionsComponentDefinition.Types.Recorder,
        CreateSessionRecorderNode,
        inputs:
        [
            ComponentPorts.Metadata<SessionContentRecordInput>(SessionsComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<SessionContentRecord>(SessionsComponentDefinition.Ports.Output)
        ],
        options: SessionsComponentDefinition.CreateOptions(SessionsComponentDefinition.Types.Recorder),
        resources: SessionsComponentDefinition.CreateResources(SessionsComponentDefinition.Types.Recorder));

    internal static ComponentDescriptor ReplayDescriptor { get; } = new(
        SessionsComponentDefinition.Types.Replay,
        CreateSessionReplayNode,
        outputs:
        [
            ComponentPorts.Metadata<SessionContentRecord>(SessionsComponentDefinition.Ports.Output)
        ],
        options: SessionsComponentDefinition.CreateOptions(SessionsComponentDefinition.Types.Replay),
        resources: SessionsComponentDefinition.CreateResources(SessionsComponentDefinition.Types.Replay));

    internal static ComponentDescriptor QueryDescriptor { get; } = new(
        SessionsComponentDefinition.Types.Query,
        CreateSessionQueryNode,
        inputs:
        [
            ComponentPorts.Metadata<SessionQueryRequest>(SessionsComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<SessionQueryOutcome>(SessionsComponentDefinition.Ports.Output)
        ],
        options: SessionsComponentDefinition.CreateOptions(SessionsComponentDefinition.Types.Query),
        resources: SessionsComponentDefinition.CreateResources(SessionsComponentDefinition.Types.Query));

    public static IServiceCollection AddSessionsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static async ValueTask<ComponentInstance> CreateSessionRecorderNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SessionRecorderOptions>();
        var clock = context.GetResource<TimeProvider>(
            SessionsComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.SessionId).ConfigureAwait(false);
        try
        {
            var node = new SessionRecorderNode(options, store.Store, clock);

            return ComponentInstance.Create(
                node,
                inputs:
                [
                    ComponentPorts.Input<SessionContentRecordInput>(
                        SessionsComponentDefinition.Ports.Input,
                        node.Input)
                ],
                outputs:
                [
                    ComponentPorts.Output<SessionContentRecord>(
                        SessionsComponentDefinition.Ports.Output,
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
            SessionsComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.SessionId).ConfigureAwait(false);
        try
        {
            var node = new SessionReplayNode(options, store.Store, clock);

            return ComponentInstance.Create(
                node,
                outputs:
                [
                    ComponentPorts.Output<SessionContentRecord>(
                        SessionsComponentDefinition.Ports.Output,
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
            SessionsComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, sessionId: null).ConfigureAwait(false);
        try
        {
            var node = new SessionQueryNode(options, store.Store, clock);

            return ComponentInstance.Create(
                node,
                inputs:
                [
                    ComponentPorts.Input<SessionQueryRequest>(
                        SessionsComponentDefinition.Ports.Input,
                        node.Input)
                ],
                outputs:
                [
                    ComponentPorts.Output<SessionQueryOutcome>(
                        SessionsComponentDefinition.Ports.Output,
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
        var key = context.GetRequiredResourceKey(SessionsComponentDefinition.Resources.Store);
        return await SessionsCompositionStoreResolver.ResolveAsync(context, key, sessionId)
            .ConfigureAwait(false);
    }
}
