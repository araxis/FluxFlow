using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Hosting;

public sealed class ApplicationRuntimeAssemblerBuilder
{
    internal ApplicationRuntimeAssemblerBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    public ApplicationRuntimeAssemblerBuilder RegisterNodes(
        Action<CompositionNodeRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.AddSingleton<ICompositionNodeRegistryContributor>(
            new DelegateNodeRegistryContributor(configure));
        return this;
    }

    public ApplicationRuntimeAssemblerBuilder RegisterNodeContributor<TContributor>()
        where TContributor : class, ICompositionNodeRegistryContributor
    {
        Services.AddSingleton<ICompositionNodeRegistryContributor, TContributor>();
        return this;
    }

    public ApplicationRuntimeAssemblerBuilder ConfigureServices(
        Action<ApplicationRuntimeServicesContext> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.AddSingleton<IApplicationRuntimeServicesContributor>(
            new DelegateRuntimeServicesContributor(configure));
        return this;
    }

    public ApplicationRuntimeAssemblerBuilder RegisterServicesContributor<TContributor>()
        where TContributor : class, IApplicationRuntimeServicesContributor
    {
        Services.AddSingleton<IApplicationRuntimeServicesContributor, TContributor>();
        return this;
    }

    public ApplicationRuntimeAssemblerBuilder Configure(
        Action<ApplicationRuntimeAssemblerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }

    private sealed class DelegateNodeRegistryContributor(
        Action<CompositionNodeRegistry> configure) : ICompositionNodeRegistryContributor
    {
        public void Configure(CompositionNodeRegistry registry) => configure(registry);
    }

    private sealed class DelegateRuntimeServicesContributor(
        Action<ApplicationRuntimeServicesContext> configure)
        : IApplicationRuntimeServicesContributor
    {
        public void Configure(ApplicationRuntimeServicesContext context) => configure(context);
    }
}
