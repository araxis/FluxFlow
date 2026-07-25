using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.Configuration;
using ApplicationWorkflowDefinition = FluxFlow.Composition.Model.WorkflowDefinition;

namespace FluxFlow.Composition.Migration;

/// <summary>
/// Converts the retired workflows/nodes/links Composition JSON shape into a
/// canonical application definition. Runtime loading remains canonical-only.
/// </summary>
public sealed class LegacyCompositionDefinitionMigrator
{
    public const string DefaultSectionName = "FluxFlow:Composition";

    public ApplicationDefinition Migrate(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        return Migrate(document.RootElement);
    }

    public ApplicationDefinition Migrate(ReadOnlySpan<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json.ToArray());
        return Migrate(document.RootElement);
    }

    public ApplicationDefinition Migrate(
        IConfiguration configuration,
        string? sectionName = DefaultSectionName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfiguration source = configuration;
        if (sectionName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
            var section = configuration.GetSection(sectionName);
            if (!section.Exists())
            {
                throw new CompositionConfigurationException(
                    $"Legacy composition section '{sectionName}' was not found.");
            }

            source = section;
        }

        try
        {
            var node = ConfigurationJsonReader.Read(source)
                ?? throw new JsonException("Legacy composition configuration is empty.");
            RestoreEmptyObjects(node);
            return Migrate(node.ToJsonString());
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or ArgumentException)
        {
            throw new CompositionConfigurationException(
                "Legacy composition configuration could not be migrated.",
                exception);
        }
    }

    private static ApplicationDefinition Migrate(JsonElement root)
    {
        var rootProperties = ReadProperties(root, "Legacy composition", "Workflows");
        rootProperties.TryGetValue("Workflows", out var workflowsElement);
        var workflows = new Dictionary<string, LegacyWorkflow>(StringComparer.Ordinal);

        if (workflowsElement.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            RequireObject(workflowsElement, "Legacy composition Workflows");
            foreach (var property in workflowsElement.EnumerateObject())
            {
                var workflowName = NormalizeName(property.Name, "Workflow name");
                if (!workflows.TryAdd(
                        workflowName,
                        ReadWorkflow(property.Value, workflowName)))
                {
                    throw new JsonException(
                        $"Legacy composition contains duplicate workflow '{workflowName}' after trimming.");
                }
            }
        }

        ApplyLinks(workflows);
        return new ApplicationDefinition(
            workflows: workflows.Select(workflow =>
                new KeyValuePair<string, ApplicationWorkflowDefinition>(
                    workflow.Key,
                    new ApplicationWorkflowDefinition(workflow.Value.Components.Select(component =>
                        new KeyValuePair<string, ComponentDefinition>(
                            component.Key,
                            new ComponentDefinition(
                                component.Value.Type,
                                component.Value.Properties)))))));
    }

    private static LegacyWorkflow ReadWorkflow(JsonElement element, string workflowName)
    {
        var subject = $"Legacy workflow '{workflowName}'";
        var workflowProperties = ReadProperties(element, subject, "Nodes", "Links");
        workflowProperties.TryGetValue("Nodes", out var nodesElement);
        workflowProperties.TryGetValue("Links", out var linksElement);
        var components = new Dictionary<string, LegacyComponent>(StringComparer.Ordinal);

        if (nodesElement.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            RequireObject(nodesElement, $"{subject} Nodes");
            foreach (var property in nodesElement.EnumerateObject())
            {
                var componentName = NormalizeName(property.Name, "Component name");
                if (!components.TryAdd(
                        componentName,
                        ReadComponent(property.Value, workflowName, componentName)))
                {
                    throw new JsonException(
                        $"{subject} contains duplicate component '{componentName}' after trimming.");
                }
            }
        }

        var links = new List<LegacyLink>();
        if (linksElement.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            if (linksElement.ValueKind != JsonValueKind.Array)
                throw new JsonException($"{subject} Links must be an array.");

            var index = 0;
            foreach (var link in linksElement.EnumerateArray())
            {
                links.Add(ReadLink(link, workflowName, index));
                index++;
            }
        }

        return new LegacyWorkflow(components, links);
    }

    private static LegacyComponent ReadComponent(
        JsonElement element,
        string workflowName,
        string componentName)
    {
        var subject = $"Legacy component '{workflowName}.{componentName}'";
        var componentProperties = ReadProperties(
            element,
            subject,
            "Type",
            "Configuration",
            "Resources");
        if (!componentProperties.TryGetValue("Type", out var typeElement))
            throw new JsonException($"{subject} requires 'Type'.");
        if (typeElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(typeElement.GetString()))
        {
            throw new JsonException($"{subject} Type must be a non-empty string.");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        componentProperties.TryGetValue("Configuration", out var configuration);
        CopyProperties(
            configuration,
            properties,
            $"{subject} Configuration");

        componentProperties.TryGetValue("Resources", out var resources);
        if (resources.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            RequireObject(resources, $"{subject} Resources");
            foreach (var resource in resources.EnumerateObject())
            {
                var name = NormalizeName(resource.Name, "Resource property name");
                if (resource.Value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(resource.Value.GetString()))
                {
                    throw new JsonException(
                        $"{subject} resource '{name}' must be a non-empty string.");
                }

                if (!properties.TryAdd(name, resource.Value.Clone()))
                {
                    throw new JsonException(
                        $"{subject} uses '{name}' in both Configuration and Resources. " +
                        "The canonical flat component shape cannot represent that ambiguity; rename one property before migrating.");
                }
            }
        }

        return new LegacyComponent(typeElement.GetString()!.Trim(), properties);
    }

    private static void CopyProperties(
        JsonElement source,
        IDictionary<string, JsonElement> target,
        string subject)
    {
        if (source.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return;

        RequireObject(source, subject);
        foreach (var property in source.EnumerateObject())
        {
            var name = NormalizeName(property.Name, "Configuration property name");
            if (!target.TryAdd(name, property.Value.Clone()))
                throw new JsonException($"{subject} contains duplicate property '{name}' after trimming.");
        }
    }

    private static LegacyLink ReadLink(
        JsonElement element,
        string workflowName,
        int index)
    {
        var subject = $"Legacy workflow '{workflowName}' Links[{index}]";
        var properties = ReadProperties(element, subject, "From", "To");
        if (!properties.TryGetValue("From", out var from))
            throw new JsonException($"{subject} requires 'From'.");
        if (!properties.TryGetValue("To", out var to))
            throw new JsonException($"{subject} requires 'To'.");
        return new LegacyLink(
            ReadPortReference(from, subject, "From"),
            ReadPortReference(to, subject, "To"));
    }

    private static LegacyPortReference ReadPortReference(
        JsonElement element,
        string subject,
        string role)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (string.IsNullOrWhiteSpace(value))
                throw new JsonException($"{subject} {role} cannot be empty.");

            var parts = value.Split('.', StringSplitOptions.TrimEntries);
            if (parts.Any(string.IsNullOrWhiteSpace))
                throw new JsonException($"{subject} {role} contains an empty address segment.");
            return parts.Length switch
            {
                2 => new LegacyPortReference(null, parts[0], parts[1]),
                3 => new LegacyPortReference(parts[0], parts[1], parts[2]),
                _ => throw new JsonException(
                    $"{subject} {role} must use 'component.port' or 'workflow.component.port'.")
            };
        }

        var referenceSubject = $"{subject} {role}";
        var properties = ReadProperties(element, referenceSubject, "Workflow", "Node", "Port");
        var workflow = ReadOptionalStringProperty(properties, "Workflow", referenceSubject);
        var component = ReadRequiredStringProperty(properties, "Node", referenceSubject);
        var port = ReadRequiredStringProperty(properties, "Port", referenceSubject);
        return new LegacyPortReference(workflow, component, port);
    }

    private static void ApplyLinks(IReadOnlyDictionary<string, LegacyWorkflow> workflows)
    {
        var declarations = new Dictionary<LinkTarget, List<string>>();
        foreach (var (declaringWorkflow, workflow) in workflows)
        {
            foreach (var link in workflow.Links)
            {
                var sourceWorkflow = link.From.Workflow ?? declaringWorkflow;
                var targetWorkflow = link.To.Workflow ?? declaringWorkflow;
                var source = GetComponent(workflows, sourceWorkflow, link.From.Component, "source");
                _ = source;
                var target = GetComponent(workflows, targetWorkflow, link.To.Component, "target");
                var targetKey = new LinkTarget(targetWorkflow, link.To.Component, link.To.Port);
                if (target.Properties.ContainsKey(link.To.Port))
                {
                    throw new JsonException(
                        $"Legacy link target '{targetKey}' conflicts with an existing component property.");
                }

                if (!declarations.TryGetValue(targetKey, out var sources))
                {
                    sources = [];
                    declarations.Add(targetKey, sources);
                }

                var sourceAddress = string.Equals(sourceWorkflow, targetWorkflow, StringComparison.Ordinal)
                    ? $"{link.From.Component}.{link.From.Port}"
                    : $"{sourceWorkflow}.{link.From.Component}.{link.From.Port}";
                sources.Add(sourceAddress);
            }
        }

        foreach (var (target, sources) in declarations)
        {
            workflows[target.Workflow].Components[target.Component].Properties.Add(
                target.Port,
                sources.Count == 1
                    ? JsonSerializer.SerializeToElement(sources[0])
                    : JsonSerializer.SerializeToElement(sources));
        }
    }

    private static LegacyComponent GetComponent(
        IReadOnlyDictionary<string, LegacyWorkflow> workflows,
        string workflowName,
        string componentName,
        string role)
    {
        if (!workflows.TryGetValue(workflowName, out var workflow) ||
            !workflow.Components.TryGetValue(componentName, out var component))
        {
            throw new JsonException(
                $"Legacy link {role} component '{workflowName}.{componentName}' does not exist.");
        }

        return component;
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadProperties(
        JsonElement element,
        string subject,
        params string[] allowedNames)
    {
        RequireObject(element, subject);
        var allowed = allowedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                throw new JsonException($"{subject} contains unknown property '{property.Name}'.");
            if (!result.TryAdd(property.Name, property.Value))
                throw new JsonException($"{subject} contains duplicate property '{property.Name}'.");
        }

        return result;
    }

    private static string? ReadOptionalStringProperty(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string subject)
    {
        if (!properties.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException($"{subject} {name} must be a string.");
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string ReadRequiredStringProperty(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string subject)
        => ReadOptionalStringProperty(properties, name, subject)
           ?? throw new JsonException($"{subject} requires a non-empty {name} string.");

    private static string NormalizeName(string value, string role)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
            throw new JsonException($"{role} cannot be empty.");
        return normalized;
    }

    private static void RequireObject(JsonElement element, string subject)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{subject} must be a JSON object.");
    }

    private static void RestoreEmptyObjects(JsonNode node)
    {
        if (node is not JsonObject root)
            return;
        RestoreEmptyObject(root, "workflows");
        if (FindObject(root, "workflows") is not { } workflows)
            return;
        foreach (var workflowName in workflows.Select(static item => item.Key).ToArray())
        {
            RestoreEmptyObject(workflows, workflowName);
            if (workflows[workflowName] is not JsonObject workflow)
                continue;
            RestoreEmptyObject(workflow, "nodes");
            RestoreEmptyArray(workflow, "links");
            if (FindObject(workflow, "nodes") is not { } nodes)
                continue;
            foreach (var componentName in nodes.Select(static item => item.Key).ToArray())
            {
                RestoreEmptyObject(nodes, componentName);
                if (nodes[componentName] is not JsonObject component)
                    continue;
                RestoreEmptyObject(component, "configuration");
                RestoreEmptyObject(component, "resources");
            }
        }
    }

    private static JsonObject? FindObject(JsonObject owner, string name)
        => owner.FirstOrDefault(property =>
                string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            .Value as JsonObject;

    private static void RestoreEmptyObject(JsonObject owner, string name)
    {
        var property = owner.FirstOrDefault(property =>
            string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase));
        if (property.Key is not null && property.Value is null)
            owner[property.Key] = new JsonObject();
    }

    private static void RestoreEmptyArray(JsonObject owner, string name)
    {
        var property = owner.FirstOrDefault(property =>
            string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase));
        if (property.Key is not null && property.Value is null)
            owner[property.Key] = new JsonArray();
    }

    private sealed record LegacyWorkflow(
        Dictionary<string, LegacyComponent> Components,
        IReadOnlyList<LegacyLink> Links);

    private sealed record LegacyComponent(
        string Type,
        Dictionary<string, JsonElement> Properties);

    private sealed record LegacyLink(
        LegacyPortReference From,
        LegacyPortReference To);

    private sealed record LegacyPortReference(
        string? Workflow,
        string Component,
        string Port);

    private readonly record struct LinkTarget(
        string Workflow,
        string Component,
        string Port)
    {
        public override string ToString() => $"{Workflow}.{Component}.{Port}";
    }
}
