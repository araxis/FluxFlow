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
                context => SessionNodeFactory.CreateRecorder(context, options)),
            new FlowNodeRegistration(
                SessionsComponentTypes.Replay,
                context => SessionNodeFactory.CreateReplay(context, options)),
            new FlowNodeRegistration(
                SessionsComponentTypes.Query,
                context => SessionNodeFactory.CreateQuery(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
