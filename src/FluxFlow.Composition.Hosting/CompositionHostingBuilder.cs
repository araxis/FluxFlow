using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition.Hosting;

public sealed class CompositionHostingBuilder
{
    internal CompositionHostingBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    public CompositionHostingBuilder RegisterNodes(Action<CompositionNodeRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.AddSingleton<ICompositionNodeRegistryContributor>(
            new DelegateCompositionNodeRegistryContributor(configure));
        return this;
    }

    public CompositionHostingBuilder Configure(Action<CompositionHostingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }
}
