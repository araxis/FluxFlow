using FluxFlow.Composition;

namespace FluxFlow.Composition.Hosting;

public sealed class StaticCompositionDefinitionSource(CompositionDefinition definition)
    : ICompositionDefinitionSource
{
    public ValueTask<CompositionDefinition> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(definition);
    }
}
