using FluxFlow.Components.Payloads.Nodes;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Payloads;

public sealed class PayloadComponentModule : IFlowNodeModule
{
    public PayloadComponentModule()
    {
        Registrations =
        [
            new FlowNodeRegistration(
                PayloadComponentTypes.Inspect,
                PayloadInspectNode.Create)
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
