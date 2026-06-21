using FluxFlow.Composition;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Composition.Hosting;

public sealed class ConfigurationCompositionDefinitionSource(
    IConfiguration configuration,
    string sectionName = CompositionConfigurationLoader.DefaultSectionName,
    CompositionConfigurationLoader? loader = null)
    : ICompositionDefinitionSource
{
    private readonly CompositionConfigurationLoader _loader = loader ?? new CompositionConfigurationLoader();

    public ValueTask<CompositionDefinition> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_loader.Load(configuration, sectionName));
    }
}
