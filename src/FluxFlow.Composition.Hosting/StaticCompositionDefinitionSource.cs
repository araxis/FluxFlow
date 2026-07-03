using FluxFlow.Composition;

namespace FluxFlow.Composition.Hosting;

public sealed class StaticCompositionDefinitionSource : ICompositionDefinitionSource
{
    private readonly CompositionDefinition _definition;

    public StaticCompositionDefinitionSource(CompositionDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public ValueTask<CompositionDefinition> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_definition);
    }
}
