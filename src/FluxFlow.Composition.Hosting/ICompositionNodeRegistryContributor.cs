using FluxFlow.Composition;

namespace FluxFlow.Composition.Hosting;

public interface ICompositionNodeRegistryContributor
{
    void Configure(CompositionNodeRegistry registry);
}
