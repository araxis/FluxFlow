using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Hosting;

public interface IApplicationDefinitionSource
{
    ValueTask<ApplicationDefinition> LoadAsync(
        CancellationToken cancellationToken = default);
}
