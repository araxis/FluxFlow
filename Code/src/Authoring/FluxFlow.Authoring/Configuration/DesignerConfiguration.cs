using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Authoring;

/// <summary>Edits configuration JSON without constructing runtime objects or rewriting untouched values.</summary>
public sealed partial class DesignerConfiguration
{
    private readonly JsonObject _root;
    private readonly JsonObject _workflows;
    private readonly string _sectionPath;

    public DesignerConfiguration(string json, string sectionPath = "")
    {
        _root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Configuration must be an object.");
        _sectionPath = sectionPath;
        var application = _root;
        foreach (var part in sectionPath.Split(':', StringSplitOptions.RemoveEmptyEntries))
            application = Object(application, part);
        _workflows = Object(application, "Workflows");
        // Use the same JSON provider as the runner, including duplicate/case-collision checks.
        using var configuration = ReadConfiguration();
        foreach (var workflow in _workflows)
        {
            _ = ComponentAddress.Create(workflow.Key, "validation");
            if (workflow.Value is not JsonObject components) throw new JsonException($"Workflow '{workflow.Key}' must be an object.");
            foreach (var component in components.Where(item => !item.Key.Equals("Resources", StringComparison.OrdinalIgnoreCase)))
            {
                _ = ComponentAddress.Create(workflow.Key, component.Key);
                if (component.Value is not JsonObject values || values[Key(values, "Type")] is not JsonValue type ||
                    !type.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
                    throw new JsonException($"Component '{workflow.Key}.{component.Key}' requires a string Type.");
            }
        }
    }

    public IEnumerable<string> WorkflowNames => _workflows.Select(item => item.Key);

    public void EnsureWorkflow(string name)
    {
        _ = ComponentAddress.Create(name, "validation");
        if (!_workflows.ContainsKey(name)) _workflows.Add(name, new JsonObject());
    }

    public IReadOnlyList<ConfigurationNode> Nodes(string workflow) => Object(_workflows, workflow, exact: true)
        .Where(item => !item.Key.Equals("Resources", StringComparison.OrdinalIgnoreCase))
        .Select(item => new ConfigurationNode(ComponentAddress.Create(workflow, item.Key),
            ((JsonObject)item.Value!)[Key((JsonObject)item.Value!, "Type")]!.GetValue<string>(),
            ((JsonObject)item.Value!).ToDictionary(property => property.Key,
                property => JsonSerializer.SerializeToElement(property.Value), StringComparer.Ordinal))).ToArray();

    public IReadOnlyList<ConfigurationLink> Links(Func<string, PaletteItemModel?> presentation,
        Func<ConfigurationNode, PaletteItemModel?>? configuredPresentation = null) =>
        ReadLinks(configuredPresentation ?? (node => presentation(node.Type)));

    private IReadOnlyList<ConfigurationLink> ReadLinks(Func<ConfigurationNode, PaletteItemModel?> presentation)
    {
        // Read only declared ports. A saved setting such as "Hot:Room" must not become a child
        // of the independent "Hot" link when the configuration provider splits keys at ':'.
        var ports = new JsonObject();
        var declared = new List<(string Path, string Key, JsonNode? Value, PortAddress Own, bool Input, bool Error)>();
        var links = new List<ConfigurationLink>();
        foreach (var workflow in WorkflowNames)
        foreach (var node in Nodes(workflow))
        {
            var item = presentation(node);
            if (item is null) continue; // Unknown component settings belong to that component's registrar.
            var values = Object(Object(_workflows, workflow, exact: true), node.Address.ComponentName, exact: true);
            foreach (var property in values)
            {
                var input = item.Inputs.Concat(item.SignalInputs).FirstOrDefault(port => port.Name.Equals(property.Key, StringComparison.OrdinalIgnoreCase));
                var output = item.Outputs.FirstOrDefault(port => port.Name.Equals(property.Key, StringComparison.OrdinalIgnoreCase));
                var error = property.Key.Equals("Error", StringComparison.OrdinalIgnoreCase) && item.Outputs.Any(port => port.Name == "Output");
                if (input is null && output is null && !error) continue;
                var path = string.Join(':', new[] { _sectionPath, "Workflows", workflow, node.Address.ComponentName, property.Key }.Where(part => part.Length != 0));
                ports.Add(path, property.Value?.DeepClone());
                declared.Add((path, property.Key, property.Value, node.Address.Port(input?.Name ?? output?.Name ?? "Output"), input is not null, error));
            }
        }
        if (declared.Count == 0) return links;
        using var configuration = ReadConfiguration(ports);
        foreach (var (path, key, value, own, input, error) in declared)
        {
            var owner = own.ComponentAddress!;
            var references = OneOrManyPortReferences.Read(configuration.GetSection(path), owner);
            for (var index = 0; index < references.Count; index++)
            {
                var reference = references[index];
                var declaration = value is JsonArray array ? array[index] : value;
                links.Add(new ConfigurationLink(input ? reference.Address : own, input ? own : reference.Address,
                    reference.Condition, reference.Engine, error, owner, key, declaration!, input));
            }
        }
        return links;
    }

    public void UpdateNode(ConfigurationNode node)
    {
        var workflow = Object(_workflows, node.Address.WorkflowName, exact: true);
        var values = workflow[node.Address.ComponentName] as JsonObject ?? new JsonObject();
        foreach (var key in values.Select(item => item.Key).Except(node.Values.Keys).ToArray()) values.Remove(key);
        foreach (var (key, value) in node.Values)
        {
            var updated = JsonNode.Parse(value.GetRawText());
            if (!JsonNode.DeepEquals(values[key], updated)) values[key] = updated;
        }
        values[Key(values, "Type")] = node.Type;
        if (values.Parent is null) workflow.Add(node.Address.ComponentName, values);
    }

    public void RemoveNodes(IReadOnlyCollection<ComponentAddress> addresses, Func<string, PaletteItemModel?> presentation,
        Func<ConfigurationNode, PaletteItemModel?>? configuredPresentation = null)
    {
        if (addresses.Count == 0) return;
        foreach (var link in Links(presentation, configuredPresentation))
            if (link.Source.ComponentAddress is { } source && addresses.Contains(source) ||
                link.Target.ComponentAddress is { } target && addresses.Contains(target))
                RemoveLink(link);
        foreach (var address in addresses)
            Object(_workflows, address.WorkflowName, exact: true).Remove(address.ComponentName);
    }

    public bool RenameNode(ComponentAddress address, string name, Func<string, PaletteItemModel?> presentation,
        Func<ConfigurationNode, PaletteItemModel?>? configuredPresentation = null)
    {
        var renamed = ComponentAddress.Create(address.WorkflowName, name);
        if (name.Contains(':'))
            throw new ArgumentException("Component names cannot contain the configuration path separator ':'.", nameof(name));
        var workflow = Object(_workflows, address.WorkflowName, exact: true);
        _ = Object(workflow, address.ComponentName, exact: true);
        if (address == renamed) return false;
        if (name.Equals("Resources", StringComparison.OrdinalIgnoreCase) || workflow.Any(item =>
            item.Key != address.ComponentName && item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("That component name is already used or reserved.", nameof(name));

        // Only registered port declarations are addresses; matching text in settings is not a link.
        var links = Links(presentation, configuredPresentation);
        foreach (var link in links)
        {
            var reference = link.IsInput ? link.Source : link.Target;
            if (reference.ComponentAddress != address) continue;
            var field = link.Declaration is JsonObject fields ? fields[Key(fields, "Address")]! : link.Declaration;
            var text = field.GetValue<string>();
            var relative = $"{name}.{reference.PortName}";
            var replacement = text.Count(character => character == '.') == 1 &&
                ApplicationAddress.TryResolvePort(relative, address.WorkflowName, out _)
                ? relative : renamed.Port(reference.PortName).Value;
            field.ReplaceWith(JsonValue.Create(replacement));
        }

        // Retain the component's position and all its original fields in the JSON object.
        var entries = workflow.ToArray();
        workflow.Clear();
        foreach (var entry in entries)
            workflow.Add(entry.Key == address.ComponentName ? name : entry.Key, entry.Value);
        return true;
    }

    public void RemoveLink(ConfigurationLink link)
    {
        if (link.Declaration.Parent is JsonArray array)
        {
            array.Remove(link.Declaration);
            if (array.Count == 0 && array.Parent is JsonObject owner) owner.Remove(link.Property);
        }
        else if (link.Declaration.Parent is JsonObject owner) owner.Remove(link.Property);
    }

    public bool UpdateLink(ConfigurationLink link, string? condition, string? engine)
    {
        if (!ReferenceEquals(link.Declaration.Root, _root))
            throw new InvalidOperationException("Select a link in the current document.");
        condition = string.IsNullOrEmpty(condition) ? null : condition;
        engine = string.IsNullOrEmpty(engine) ? null : engine;
        if (condition == link.Condition && engine == link.Engine) return false;

        var updated = link.Declaration is JsonObject original ? (JsonObject)original.DeepClone()
            : new JsonObject { ["Address"] = link.Declaration.DeepClone() };
        Set("Condition", condition);
        Set("Engine", engine);

        // Validate the replacement with the native reader before changing the retained document.
        var path = string.Join(':', new[] { _sectionPath, "Workflows", link.Owner.WorkflowName,
            link.Owner.ComponentName, link.Property }.Where(part => part.Length != 0));
        if (link.Declaration.Parent is JsonArray declarations) path += $":{declarations.IndexOf(link.Declaration)}";
        var candidate = new JsonObject { [path] = updated.DeepClone() };
        using var configuration = ReadConfiguration(candidate);
        _ = PortReference.Read(configuration.GetSection(path), link.Owner);

        if (link.Declaration.Parent is JsonArray array) array[array.IndexOf(link.Declaration)] = updated;
        else ((JsonObject)link.Declaration.Parent!)[link.Property] = updated;
        return true;

        void Set(string name, string? value)
        {
            var key = Key(updated, name);
            if (value is null) updated.Remove(key);
            else updated[key] = value;
        }
    }

    public void AddLink(PortAddress source, PortAddress target, ConfigurationLink? previous = null, bool? isError = null)
    {
        if (target.IsSystem) throw new ArgumentException("System ports cannot be link targets.", nameof(target));
        var error = isError ?? previous?.IsError == true;
        var fromInput = previous?.IsInput == true && !error;
        var ownerAddress = (fromInput ? target : source).ComponentAddress
            ?? throw new ArgumentException("A new link must be declared on a workflow component.");
        var owner = Object(Object(_workflows, ownerAddress.WorkflowName, exact: true), ownerAddress.ComponentName, exact: true);
        var property = error ? "Error" : (fromInput ? target : source).PortName;
        if (previous is not null && previous.Property.Equals(property, StringComparison.OrdinalIgnoreCase))
            property = previous.Property;
        var reference = (fromInput ? source : target).Value;
        JsonNode declaration = JsonValue.Create(reference)!;
        if (previous?.Declaration is JsonObject original)
        {
            var fields = (JsonObject)original.DeepClone();
            fields[Key(fields, "Address")] = reference;
            declaration = fields;
        }
        var key = Key(owner, property);
        if (!owner.ContainsKey(key)) owner[key] = declaration;
        else if (owner[key] is JsonArray array) array.Add(declaration);
        else owner[key] = new JsonArray(owner[key]?.DeepClone(), declaration);
    }

    public string ToJson() => _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    public string ToApplicationJson()
    {
        var application = _root;
        foreach (var part in _sectionPath.Split(':', StringSplitOptions.RemoveEmptyEntries))
            application = Object(application, part);
        return application.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string Key(JsonObject value, string name) => value.Select(item => item.Key)
        .FirstOrDefault(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;

    private static JsonObject Object(JsonObject value, string name, bool exact = false) =>
        value[exact ? name : Key(value, name)] as JsonObject ?? throw new JsonException($"'{name}' must be an object.");

    internal ConfigurationRoot ReadConfiguration() => ReadConfiguration(_root);

    private static ConfigurationRoot ReadConfiguration(JsonObject document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString()));
        return (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
    }
}

public sealed record ConfigurationNode(ComponentAddress Address, string Type, Dictionary<string, JsonElement> Values);

/// <summary>A link projection retaining its exact original declaration for targeted edits.</summary>
public sealed record ConfigurationLink(PortAddress Source, PortAddress Target, string? Condition, string? Engine,
    bool IsError, ComponentAddress Owner, string Property, JsonNode Declaration, bool IsInput);
