using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Application.Addressing;

namespace FluxFlow.Authoring;

public sealed partial class DesignerConfiguration
{
    public IReadOnlyList<ConfigurationResource> Resources()
    {
        var result = new List<ConfigurationResource>();
        Add(Application(), null);
        foreach (var workflow in _workflows) Add(workflow.Value!.AsObject(), workflow.Key);
        return result;

        void Add(JsonObject owner, string? workflow)
        {
            if (owner[Key(owner, "Resources")] is not JsonObject resources) return;
            Visit(resources, "", workflow);
        }
        void Visit(JsonObject resources, string prefix, string? workflow)
        {
            foreach (var (key, value) in resources)
                if (value is JsonObject settings)
                {
                    var name = prefix + key;
                    if (settings.ContainsKey(Key(settings, "Type"))) result.Add(new(name, workflow, (JsonObject)settings.DeepClone()));
                    else Visit(settings, name + ".", workflow);
                }
        }
    }

    public void SetResource(string name, string? workflow, JsonObject settings, bool add = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains(':')) throw new ArgumentException("Connection names cannot contain ':'.");
        var path = name.Split('.');
        _ = workflow is null ? ApplicationAddress.Resource(path) : ApplicationAddress.WorkflowResource(workflow, path);
        if (settings[Key(settings, "Type")] is not JsonValue type || !type.TryGetValue<string>(out var typeName) || string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("A connection requires a string Type.");
        var owner = workflow is null ? Application() : Object(_workflows, workflow, exact: true);
        var resourceKey = Key(owner, "Resources");
        var resources = owner[resourceKey] as JsonObject ?? new JsonObject();
        if (owner[resourceKey] is not null && owner[resourceKey] is not JsonObject) throw new JsonException("Resources must be an object.");
        if (owner[resourceKey] is null) owner[resourceKey] = resources;
        foreach (var segment in path[..^1])
        {
            var groupKey = Key(resources, segment);
            if (resources[groupKey] is null && add) resources[groupKey] = new JsonObject();
            resources = Object(resources, groupKey);
            if (resources.ContainsKey(Key(resources, "Type"))) throw new ArgumentException("A connection cannot contain another connection.");
        }
        var key = Key(resources, path[^1]);
        if (add && resources.ContainsKey(key)) throw new ArgumentException("A connection with that name already exists in this scope.");
        if (!add && !resources.ContainsKey(key)) throw new ArgumentException("The connection no longer exists.");
        resources[key] = settings.DeepClone();
        using var configuration = ReadConfiguration();
    }

    private JsonObject Application()
    {
        var result = _root;
        foreach (var part in _sectionPath.Split(':', StringSplitOptions.RemoveEmptyEntries)) result = Object(result, part);
        return result;
    }

    public IReadOnlyList<string> ResourceUsages(ConfigurationResource resource, IEnumerable<IComponentDescription> descriptions)
        => ResourceReferences(descriptions).Where(reference => reference.Resource.Address == resource.Address)
            .Select(reference => $"{reference.Node.Address} · {reference.Label}").ToArray();

    public ConfigurationResource RenameResource(string name, string? workflow, string newName, IEnumerable<IComponentDescription> descriptions)
    {
        var resource = Resources().SingleOrDefault(item => item.Name == name && item.Workflow == workflow)
            ?? throw new ArgumentException("The connection no longer exists.");
        newName = newName.Trim();
        if (newName == name) return resource;
        var references = ResourceReferences(descriptions).ToArray();
        RemoveResourceValue(resource);
        SetResource(newName, workflow, resource.Settings, add: true);
        var renamed = Resources().Single(item => item.Name.Equals(newName, StringComparison.OrdinalIgnoreCase) && item.Workflow == workflow);
        // Explicit addresses prevent a new name from shadowing another scoped connection.
        foreach (var reference in references)
        {
            var target = reference.Resource.Address == resource.Address ? renamed : reference.Resource;
            var current = ResolveResource(reference.Node.Values[reference.Key].GetString()!, reference.Node.Address.WorkflowName,
                target.Type, Resources());
            if (reference.Resource.Address != resource.Address && current?.Address == target.Address) continue;
            reference.Node.Values[reference.Key] = JsonSerializer.SerializeToElement(target.Address);
            UpdateNode(reference.Node);
        }
        return renamed;
    }

    public void RemoveResource(string name, string? workflow, IEnumerable<IComponentDescription> descriptions)
    {
        var resource = Resources().SingleOrDefault(item => item.Name == name && item.Workflow == workflow)
            ?? throw new ArgumentException("The connection no longer exists.");
        foreach (var reference in ResourceReferences(descriptions).Where(item => item.Resource.Address == resource.Address).ToArray())
        {
            // Retain a missing reference rather than falling back to a same-named resource in another scope.
            reference.Node.Values[reference.Key] = JsonSerializer.SerializeToElement(resource.Address);
            UpdateNode(reference.Node);
        }
        RemoveResourceValue(resource);
    }

    private void RemoveResourceValue(ConfigurationResource resource)
    {
        var owner = resource.Workflow is null ? Application() : Object(_workflows, resource.Workflow, exact: true);
        var resources = Object(owner, "Resources");
        var path = resource.Name.Split('.');
        foreach (var segment in path[..^1]) resources = Object(resources, segment);
        resources.Remove(Key(resources, path[^1]));
    }

    private static ConfigurationResource? ResolveResource(string value, string workflow, string type, IReadOnlyList<ConfigurationResource> resources)
    {
        try
        {
            return ApplicationAddress.ResolveResourcePaths(value.Trim(), workflow)
                .Select(candidate => resources.FirstOrDefault(item => item.Address == candidate.Value && item.Type == type))
                .FirstOrDefault(item => item is not null);
        }
        catch (Exception error) when (error is ArgumentException or FormatException) { return null; }
    }

    private IEnumerable<(ConfigurationNode Node, string Key, string Label, ConfigurationResource Resource)> ResourceReferences(IEnumerable<IComponentDescription> descriptions)
    {
        var registered = descriptions.ToDictionary(item => item.Palette.ComponentType, StringComparer.Ordinal);
        var resources = Resources();
        foreach (var workflow in WorkflowNames)
        foreach (var node in Nodes(workflow))
        {
            if (!registered.TryGetValue(node.Type, out var description)) continue;
            foreach (var option in description.GetInspector(node.Values).Sections.SelectMany(section => section.Options).Where(option => option.RelatedResource is not null))
            {
                var field = node.Values.FirstOrDefault(pair => pair.Key.Equals(option.Name, StringComparison.OrdinalIgnoreCase));
                var value = field.Value;
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) continue;
                if (ResolveResource(value.GetString()!, workflow, option.RelatedResource!, resources) is { } resolved)
                    yield return (node, field.Key, option.DisplayName, resolved);
            }
        }
    }
}

public sealed record ConfigurationResource(string Name, string? Workflow, JsonObject Settings)
{
    public string Path => Workflow is null ? $"Resources:{Name.Replace('.', ':')}" : $"Workflows:{Workflow}:Resources:{Name.Replace('.', ':')}";
    public string Address => Workflow is null ? $"Resources.{Name}" : $"{Workflow}.Resources.{Name}";
    public string Scope => Workflow is null ? "Application · shared across workflows" : $"Workflow · {Workflow}";
    public string Type => Settings[DesignerConfiguration.Key(Settings, "Type")] is JsonValue value && value.TryGetValue<string>(out var type) ? type : "";
}
