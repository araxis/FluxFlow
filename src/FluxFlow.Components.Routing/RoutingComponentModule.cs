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
                context => RoutingNodeFactory.CreateSwitch(context, options)),
            new FlowNodeRegistration(
                RoutingComponentTypes.Correlation,
                context => RoutingNodeFactory.CreateCorrelation(context, options)),
            new FlowNodeRegistration(
                RoutingComponentTypes.Window,
                context => RoutingNodeFactory.CreateWindow(context, options)),
            new FlowNodeRegistration(
                RoutingComponentTypes.Join,
                context => RoutingNodeFactory.CreateJoin(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
