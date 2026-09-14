using FluxFlow.Composition.Definition;
using FluxFlow.Composition;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using FluxFlow.Engine.Ports;
using FluxFlow.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimePlanFactory(
    IServiceProvider hostServices,
    ApplicationRuntimePortSurfaceFactory portSurfaces)
{
    internal IReadOnlyDictionary<string, ComponentFactory> Factories(ApplicationSetup setup)
        => FluxFlowRegistrationExtensions.CreateFactoryMap(hostServices, setup.ComponentFactories);

    internal ApplicationRuntimePlan Create(ApplicationSetup setup, IReadOnlyDictionary<string, ComponentInstance> instances)
    {
        var compilation = new ApplicationLinkCompiler(
                instances,
                hostServices.GetService<IFlowExpressionEngine>(),
                ApplicationPortRuntimeBuilder.SystemOutputs,
                name => hostServices.GetKeyedService<IFlowExpressionEngine>(name))
            .Compile(setup);
        if (!compilation.IsValid)
            throw new ApplicationRuntimeAssemblerException(compilation.Diagnostics);

        return new ApplicationRuntimePlan(
            compilation.Links,
            portSurfaces.Describe(setup.Definition, instances),
            compilation.DiscardedOutputs);
    }
}

internal sealed record ApplicationRuntimePlan(
    IReadOnlyList<CompiledApplicationLink> Links,
    IReadOnlyList<ApplicationRuntimePortSurfaceEntry> Surface,
    IReadOnlyList<FluxFlow.Core.Addressing.ApplicationAddress> DiscardedOutputs);
