using FluxFlow.Composition;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using FluxFlow.Engine.Ports;
using FluxFlow.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimePlanFactory(
    ComponentCatalog hostCatalog,
    IServiceProvider hostServices,
    ApplicationRuntimePortSurfaceFactory portSurfaces)
{
    internal ApplicationRuntimePlan Create(ApplicationSetup setup)
    {
        ComponentCatalog catalog;
        try
        {
            catalog = hostCatalog.Merge(setup.ComponentDescriptors);
        }
        catch (InvalidOperationException exception)
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Application component catalog is invalid: {exception.Message}",
                exception);
        }

        var compilation = new ApplicationLinkCompiler(
                catalog,
                hostServices.GetService<IFlowExpressionEngine>(),
                ApplicationPortRuntimeBuilder.SystemOutputs,
                name => hostServices.GetKeyedService<IFlowExpressionEngine>(name))
            .Compile(setup);
        if (!compilation.IsValid)
            throw new ApplicationRuntimeAssemblerException(compilation.Diagnostics);

        return new ApplicationRuntimePlan(
            catalog,
            compilation.Links,
            portSurfaces.Describe(setup.Definition, catalog),
            compilation.DiscardedOutputs, compilation.ReplyOutputs);
    }
}

internal sealed record ApplicationRuntimePlan(
    ComponentCatalog Catalog,
    IReadOnlyList<CompiledApplicationLink> Links,
    IReadOnlyList<ApplicationRuntimePortSurfaceEntry> Surface,
    IReadOnlyList<FluxFlow.Core.Addressing.ApplicationAddress> DiscardedOutputs,
    IReadOnlyList<FluxFlow.Core.Addressing.ApplicationAddress> ReplyOutputs);
