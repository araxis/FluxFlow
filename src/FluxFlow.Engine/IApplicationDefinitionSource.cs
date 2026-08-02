using FluxFlow.Composition.Model;

namespace FluxFlow.Engine;

public interface IApplicationDefinitionSource
{
    ValueTask<ApplicationDefinition> LoadAsync(
        CancellationToken cancellationToken = default);
}
