using FluxFlow.Composition.Model;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Composition.Hosting;

[Obsolete("Use ConfigurationApplicationDefinitionSource from FluxFlow.Engine.")]
public sealed class ConfigurationApplicationDefinitionSource : IApplicationDefinitionSource
{
    private readonly IConfiguration _configuration;
    private readonly string? _sectionName;
    private readonly ApplicationDefinitionConfigurationLoader _loader;

    public ConfigurationApplicationDefinitionSource(
        IConfiguration configuration,
        string? sectionName = null,
        ApplicationDefinitionConfigurationLoader? loader = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        if (sectionName is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        _sectionName = sectionName;
        _loader = loader ?? new ApplicationDefinitionConfigurationLoader();
    }

    public ValueTask<ApplicationDefinition> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_loader.Load(_configuration, _sectionName));
    }
}
