using System.Text.Json;
using FluxFlow.Core.Addressing;

namespace FluxFlow.Json.Model;

public sealed class ResourceInstanceDefinition : ResourceDefinition
{
    /// <summary>Enumerates application and workflow resources using their full addresses.</summary>
    public static IEnumerable<KeyValuePair<ApplicationAddress, ResourceInstanceDefinition>> Enumerate(ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        foreach (var (path, resource) in Enumerate(definition.Resources))
            yield return new(ApplicationAddress.Resource(path.Split('.')), resource);
        foreach (var (workflow, value) in definition.Workflows)
            foreach (var (path, resource) in Enumerate(value.Resources))
                yield return new(ApplicationAddress.WorkflowResource(workflow, path.Split('.')), resource);
    }

    internal IReadOnlyList<string>? PropertyOrder { get; init; }

    public ResourceInstanceDefinition(
        string type,
        IEnumerable<KeyValuePair<string, JsonElement>>? properties = null)
    {
        Type = DefinitionRules.RequireType(type, nameof(type));
        Properties = DefinitionRules.CopyProperties(
            properties,
            nameof(properties),
            rejectLegacyComponentWrappers: false);
    }

    public string Type { get; }

    public IReadOnlyDictionary<string, JsonElement> Properties { get; }

    public override JsonElement Value => JsonSerializer.SerializeToElement(
        (PropertyOrder ?? new[] { "Type" }.Concat(Properties.Keys).ToArray()).ToDictionary(
            name => name, name => name == "Type" ? JsonSerializer.SerializeToElement(Type) : Properties[name]));

    /// <summary>Reads connection definitions from ordinary nested JSON objects.</summary>
    public static IEnumerable<KeyValuePair<string, ResourceInstanceDefinition>> Enumerate(
        IReadOnlyDictionary<string, ResourceDefinition> resources)
    {
        foreach (var (name, resource) in resources)
        {
            foreach (var item in Visit(resource.Value, name)) yield return item;
        }

        static IEnumerable<KeyValuePair<string, ResourceInstanceDefinition>> Visit(JsonElement value, string path)
        {
            if (value.ValueKind != JsonValueKind.Object) yield break;
            var properties = value.EnumerateObject().ToArray();
            var types = properties.Where(p => p.Name.Equals("Type", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (types.Length > 1) throw new JsonException($"Resource '{path}' contains duplicate Type properties.");
            var type = types.FirstOrDefault();
            if (type.Value.ValueKind != JsonValueKind.Undefined)
            {
                if (type.Value.ValueKind != JsonValueKind.String) throw new JsonException($"Resource '{path}' Type must be a string.");
                yield return new(path, new ResourceInstanceDefinition(type.Value.GetString()!, properties
                    .Where(p => !p.Name.Equals("Type", StringComparison.OrdinalIgnoreCase)).Select(p => KeyValuePair.Create(p.Name, p.Value))));
                yield break;
            }
            foreach (var property in properties)
                foreach (var item in Visit(property.Value, $"{path}.{property.Name}")) yield return item;
        }
    }
}
