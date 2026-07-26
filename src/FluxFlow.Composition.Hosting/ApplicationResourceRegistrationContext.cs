using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition.Hosting;

public sealed class ApplicationResourceRegistrationContext
{
    internal ApplicationResourceRegistrationContext(
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
