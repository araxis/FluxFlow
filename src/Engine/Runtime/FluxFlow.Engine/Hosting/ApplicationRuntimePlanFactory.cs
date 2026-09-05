using FluxFlow.Composition;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Ports;
using FluxFlow.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimePlanFactory(
    ComponentCatalog hostCatalog,
    IServiceProvider hostServices,
    ApplicationRuntimePortSurfaceFactory portSurfaces)
{
    internal ApplicationRuntimePlan Create(ApplicationDefinition definition)
    {
        ComponentCatalog catalog;
        try
        {
            catalog = hostCatalog.Merge(definition.ComponentDescriptors);
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
                ApplicationPortRuntimeBuilder.SystemOutputs)
            .Compile(definition);
        if (!compilation.IsValid)
            throw new ApplicationRuntimeAssemblerException(compilation.Diagnostics);

        return new ApplicationRuntimePlan(
            catalog,
            compilation.Links,
            portSurfaces.Describe(definition, catalog));
    }
}

internal sealed record ApplicationRuntimePlan(
    ComponentCatalog Catalog,
    IReadOnlyList<CompiledApplicationLink> Links,
    IReadOnlyList<ApplicationRuntimePortSurfaceEntry> Surface);
