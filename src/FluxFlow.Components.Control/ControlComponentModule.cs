using FluxFlow.Components.Control.Nodes;
using FluxFlow.Components.Control.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Control;

public sealed class ControlComponentModule : IFlowNodeModule
{
    public ControlComponentModule(ControlComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                ControlComponentTypes.Filter,
                context => ControlNodeFactory.CreateFilter(context, options)),
            new FlowNodeRegistration(
                ControlComponentTypes.When,
                context => ControlNodeFactory.CreateWhen(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
