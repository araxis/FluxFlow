using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;

namespace FluxFlow.Components.Mqtt.Composition;

internal sealed class MqttCompositionResourceIndex
{
    private readonly IReadOnlyDictionary<ApplicationAddress, MqttIndexedResource> _resources;

    private MqttCompositionResourceIndex(
        IReadOnlyDictionary<ApplicationAddress, MqttIndexedResource> resources)
    {
        _resources = resources;
    }

    internal IEnumerable<MqttIndexedResource> OrderedResources =>
        _resources.Values.OrderBy(static value => value.Address.Value, StringComparer.Ordinal);

    internal static MqttCompositionResourceIndex Create(ApplicationDefinition definition)
    {
        var resources = new Dictionary<ApplicationAddress, MqttIndexedResource>();
        foreach (var (name, resource) in definition.Resources)
            Flatten([name], resource, resources);
        return new MqttCompositionResourceIndex(resources);
    }

    internal void RequireType(
        ApplicationAddress reference,
        string expectedType,
        ApplicationAddress owner)
    {
        if (!_resources.TryGetValue(reference, out var resource))
        {
            throw new InvalidOperationException(
                $"MQTT resource '{owner}' references missing resource '{reference}'.");
        }
        if (!string.Equals(resource.Definition.Type, expectedType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MQTT resource '{owner}' references '{reference}' as '{expectedType}', " +
                $"but its type is '{resource.Definition.Type}'.");
        }
    }

    private static void Flatten(
        IReadOnlyList<string> path,
        ResourceDefinition resource,
        IDictionary<ApplicationAddress, MqttIndexedResource> result)
    {
        if (resource is ResourceInstanceDefinition instance)
        {
            var address = ApplicationAddress.Resource(path.ToArray());
            result.Add(address, new MqttIndexedResource(address, instance));
            return;
        }

        var group = (ResourceGroupDefinition)resource;
        foreach (var (name, child) in group.Resources)
            Flatten([.. path, name], child, result);
    }
}

internal sealed record MqttIndexedResource(
    ApplicationAddress Address,
    ResourceInstanceDefinition Definition);
