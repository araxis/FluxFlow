using FluxFlow.Composition;

namespace FluxFlow.Composition.Hosting;

internal sealed class DelegateCompositionNodeRegistryContributor(
    Action<CompositionNodeRegistry> configure)
    : ICompositionNodeRegistryContributor
{
    public void Configure(CompositionNodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        configure(registry);
    }
}
