using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    public CompositionHostingBuilder RegisterNodeContributor<TContributor>()
        where TContributor : class, ICompositionNodeRegistryContributor
    {
        Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICompositionNodeRegistryContributor, TContributor>());
        return this;
    }

    public CompositionHostingBuilder RegisterNodeContributor(
        ICompositionNodeRegistryContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICompositionNodeRegistryContributor>(contributor));
        return this;
    }

    public CompositionHostingBuilder Configure(Action<CompositionHostingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }
}
