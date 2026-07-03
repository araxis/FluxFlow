using FluxFlow.Composition;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Composition.Hosting;

public sealed class ConfigurationCompositionDefinitionSource : ICompositionDefinitionSource
{
    private readonly IConfiguration _configuration;
    private readonly CompositionConfigurationLoader _loader;
    private readonly string _sectionName;

    public ConfigurationCompositionDefinitionSource(
        IConfiguration configuration,
        string sectionName = CompositionConfigurationLoader.DefaultSectionName,
        CompositionConfigurationLoader? loader = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _sectionName = sectionName ?? throw new ArgumentNullException(nameof(sectionName));
        _loader = loader ?? new CompositionConfigurationLoader();
    }

    public ValueTask<CompositionDefinition> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_loader.Load(_configuration, _sectionName));
    }
}
