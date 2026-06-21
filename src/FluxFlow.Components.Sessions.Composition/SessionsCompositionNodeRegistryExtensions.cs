using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;

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

    private static ValueTask<ComposedNode> CreateSessionRecorderNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SessionRecorderOptions>();
        var store = context.GetRequiredResource<ISessionStore>(
            SessionsCompositionResourceNames.Store);
        var clock = context.GetResource<TimeProvider>(
            SessionsCompositionResourceNames.Clock);
        var node = new SessionRecorderNode(options, store, clock);

        return ValueTask.FromResult(ComposedNode.Create(
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
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateSessionReplayNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SessionReplayOptions>();
        var store = context.GetRequiredResource<ISessionStore>(
            SessionsCompositionResourceNames.Store);
        var clock = context.GetResource<TimeProvider>(
            SessionsCompositionResourceNames.Clock);
        var node = new SessionReplayNode(options, store, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<SessionRecord>(
                    SessionsCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateSessionQueryNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SessionQueryOptions>();
        var store = context.GetRequiredResource<ISessionStore>(
            SessionsCompositionResourceNames.Store);
        var clock = context.GetResource<TimeProvider>(
            SessionsCompositionResourceNames.Clock);
        var node = new SessionQueryNode(options, store, clock);

        return ValueTask.FromResult(ComposedNode.Create(
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
            errors: node.Errors));
    }
}
