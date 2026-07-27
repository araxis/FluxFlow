using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Hosting;

[Obsolete("Use StaticApplicationDefinitionSource from FluxFlow.Engine.")]
public sealed class StaticApplicationDefinitionSource(
    ApplicationDefinition definition) : IApplicationDefinitionSource
{
    private readonly ApplicationDefinition _definition =
        definition ?? throw new ArgumentNullException(nameof(definition));

    public ValueTask<ApplicationDefinition> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_definition);
    }
}
