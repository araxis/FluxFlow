namespace FluxFlow.Composition;

#pragma warning disable CS0618 // Compatibility source intentionally returns legacy definitions.
public interface ICompositionDefinitionSource
{
    ValueTask<CompositionDefinition> LoadAsync(CancellationToken cancellationToken = default);
}
#pragma warning restore CS0618
