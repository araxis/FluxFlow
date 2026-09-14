using FluxFlow.Core.Addressing;
using FluxFlow.Json.Model;

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
        foreach (var (address, instance) in ResourceInstanceDefinition.Enumerate(definition))
        {
            resources.Add(address, new MqttIndexedResource(address, instance));
        }
        return new MqttCompositionResourceIndex(resources);
    }

    internal ApplicationAddress Resolve(string reference, string expectedType, ApplicationAddress owner)
    {
        var workflow = owner.Segments[0] == "Resources" ? null : owner.Segments[0];
        var candidates = ApplicationAddress.ResolveResourcePaths(reference, workflow);
        foreach (var candidate in candidates)
        {
            if (!_resources.ContainsKey(candidate)) continue;
            RequireType(candidate, expectedType, owner);
            return candidate;
        }
        throw new InvalidOperationException($"MQTT resource '{owner}' references missing resource '{reference}'; checked {string.Join(", ", candidates)}.");
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

}

internal sealed record MqttIndexedResource(
    ApplicationAddress Address,
    ResourceInstanceDefinition Definition);
