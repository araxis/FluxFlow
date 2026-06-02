using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Routing;

public sealed class RoutingComponentModule : IFlowNodeModule
{
    public RoutingComponentModule(RoutingComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                RoutingComponentTypes.Switch,
                context => RoutingNodeFactory.CreateSwitch(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
