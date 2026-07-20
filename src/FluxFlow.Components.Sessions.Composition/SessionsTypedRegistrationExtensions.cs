using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Composition;

namespace FluxFlow.Components.Sessions.Composition;

public static class SessionsTypedRegistrationExtensions
{
    public static CompositionNodeRegistry RegisterSessionRecordOutput(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        return registry.Register(
            nodeType,
            SessionsTypedNodeFactories.CreateRecorder,
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

    public static CompositionNodeRegistry RegisterSessionReplayRecords(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        return registry.Register(
            nodeType,
            SessionsTypedNodeFactories.CreateReplay,
            outputs:
            [
                CompositionPorts.Metadata<SessionRecord>(
                    SessionsCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterSessionQueryResultBranches(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        return registry.Register(
            nodeType,
            SessionsTypedNodeFactories.CreateQuery,
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
}
