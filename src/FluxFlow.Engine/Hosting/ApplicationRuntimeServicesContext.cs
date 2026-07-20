using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Hosting;

public sealed class ApplicationRuntimeServicesContext
{
    internal ApplicationRuntimeServicesContext(
        ApplicationDefinition definition,
        ApplicationRevisionPreparationContext revision,
        IServiceProvider hostServices,
        IServiceCollection services)
    {
        Definition = definition;
        Revision = revision;
        HostServices = hostServices;
        Services = services;
    }

    public ApplicationDefinition Definition { get; }

    public ApplicationRevisionPreparationContext Revision { get; }

    public IServiceProvider HostServices { get; }

    public IServiceCollection Services { get; }
}
