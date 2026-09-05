namespace FluxFlow.Composition.Model;

public sealed class ResourceGroupDefinition : ResourceDefinition
{
    public ResourceGroupDefinition(
        IEnumerable<KeyValuePair<string, ResourceDefinition>>? resources = null)
    {
        Resources = DefinitionRules.CopyNamed(
            resources,
            nameof(resources),
            DefinitionRules.RequireResourceName);
    }

    public IReadOnlyDictionary<string, ResourceDefinition> Resources { get; }
}
