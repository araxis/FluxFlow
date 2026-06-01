using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.State;

public sealed class StateComponentModule : IFlowNodeModule
{
    public StateComponentModule(StateComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                StateComponentTypes.Reducer,
                context => StateReducerNode.Create(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
