using FluxFlow.Components.Sessions.Nodes;
using FluxFlow.Components.Sessions.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Sessions;

public sealed class SessionsComponentModule : IFlowNodeModule
{
    public SessionsComponentModule(SessionsComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                SessionsComponentTypes.Recorder,
                context => SessionRecorderNode.Create(context, options)),
            new FlowNodeRegistration(
                SessionsComponentTypes.Replay,
                context => SessionReplayNode.Create(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
