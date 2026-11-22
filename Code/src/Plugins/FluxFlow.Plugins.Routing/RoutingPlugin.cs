using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.Gate;
using FluxFlow.Authoring.Definitions.Router;
using FluxFlow.Authoring.Definitions.ChangeFilter;
using FluxFlow.Authoring.Definitions.Deduplicate;
using FluxFlow.Configuration.Components.Filtering;
using FluxFlow.Configuration.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Routing;

public sealed class RoutingPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = new("FluxFlow.Plugins.Routing",
        [Describe(new GateDescription()), Describe(new RouterDescription()) with { NamedOutputs = RouterDescription.Outputs },
         Describe(new ChangeFilterDescription()), Describe(new DeduplicateDescription())], [])
    { Design = new(typeof(RoutingDesign).Assembly.GetName().Name!, typeof(RoutingDesign).FullName!, []) };

    public void ConfigureWorkflow(IServiceCollection services) => services.AddGate().AddRouter().AddChangeFilter().AddDeduplicate();

    private static PluginComponent Describe(IComponentDescription component) =>
        new(component.Palette, component.Inspector, component.Documentation, "properties");
}
