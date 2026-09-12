using FluxFlow.Composition;

namespace FluxFlow.Engine;

public interface IApplicationDefinitionSource
{
    ValueTask<ApplicationSetup> LoadAsync(
        CancellationToken cancellationToken = default);
}
