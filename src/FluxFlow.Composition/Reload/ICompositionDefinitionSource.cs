namespace FluxFlow.Composition;

public interface ICompositionDefinitionSource
{
    ValueTask<CompositionDefinition> LoadAsync(CancellationToken cancellationToken = default);
}
