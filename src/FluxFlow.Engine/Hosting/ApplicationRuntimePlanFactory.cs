using FluxFlow.Composition;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Ports;
using FluxFlow.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimePlanFactory(
    ComponentCatalog registry,
    IServiceProvider hostServices,
    ApplicationRuntimePortSurfaceFactory portSurfaces)
{
    internal ApplicationRuntimePlan Create(ApplicationDefinition definition)
    {
        var compilation = new ApplicationLinkCompiler(
                registry,
                hostServices.GetService<IFlowExpressionEngine>(),
                ApplicationPortRuntimeBuilder.SystemOutputs)
            .Compile(definition);
        if (!compilation.IsValid)
            throw new ApplicationRuntimeAssemblerException(compilation.Diagnostics);

        return new ApplicationRuntimePlan(
            compilation.Links,
            portSurfaces.Describe(definition));
    }
}

internal sealed record ApplicationRuntimePlan(
    IReadOnlyList<CompiledApplicationLink> Links,
    IReadOnlyList<ApplicationRuntimePortSurfaceEntry> Surface);
