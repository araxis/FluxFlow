using System.Text.Json;
using FluxFlow.Composition.Model;
using ApplicationWorkflowDefinition = FluxFlow.Composition.Model.WorkflowDefinition;

namespace FluxFlow.Engine.Migration;

/// <summary>
/// Converts the retired Engine Resources/Workflows/Nodes document into the
/// canonical application model. Executable resource nodes and non-default
/// phases require explicit host migration and are rejected.
/// </summary>
public sealed class LegacyEngineApplicationDefinitionMigrator
{
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

    private static ApplicationDefinition Migrate(JsonElement root)
    {
        var rootProperties = ReadProperties(root, "Legacy Engine application", "Resources", "Workflows");
        if (rootProperties.TryGetValue("Resources", out var resources) &&
            resources.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            RequireObject(resources, "Legacy Engine Resources");
            if (resources.EnumerateObject().Any())
            {
                throw new JsonException(
                    "Legacy executable resource nodes cannot be migrated automatically. " +
                    "Register equivalent canonical resources through a host service contributor.");
            }
        }

        var workflows = new Dictionary<string, ApplicationWorkflowDefinition>(StringComparer.Ordinal);
        if (rootProperties.TryGetValue("Workflows", out var workflowElement) &&
            workflowElement.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            RequireObject(workflowElement, "Legacy Engine Workflows");
            foreach (var property in workflowElement.EnumerateObject())
            {
                var workflowName = NormalizeName(property.Name, "Workflow name");
                if (!workflows.TryAdd(workflowName, ReadWorkflow(property.Value, workflowName)))
                {
                    throw new JsonException(
                        $"Legacy Engine application contains duplicate workflow '{workflowName}' after trimming.");
                }
            }
        }

        return new ApplicationDefinition(workflows: workflows);
    }

    private static ApplicationWorkflowDefinition ReadWorkflow(
        JsonElement element,
        string workflowName)
    {
        var subject = $"Legacy Engine workflow '{workflowName}'";
        var workflowProperties = ReadProperties(element, subject, "Nodes");
        var components = new Dictionary<string, ComponentDefinition>(StringComparer.Ordinal);
        if (workflowProperties.TryGetValue("Nodes", out var nodes) &&
            nodes.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            RequireObject(nodes, $"{subject} Nodes");
            foreach (var property in nodes.EnumerateObject())
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

        return new ApplicationWorkflowDefinition(components);
    }

    private static ComponentDefinition ReadComponent(
        JsonElement element,
        string workflowName,
        string componentName)
    {
        var subject = $"Legacy Engine component '{workflowName}.{componentName}'";
        RequireObject(element, subject);

        JsonElement type = default;
        JsonElement configuration = default;
        string? defaultCondition = null;
        var phase = 0;
        var ports = new List<JsonProperty>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new JsonException($"{subject} contains duplicate property '{property.Name}'.");

            if (string.Equals(property.Name, "Type", StringComparison.OrdinalIgnoreCase))
            {
                type = property.Value;
            }
            else if (string.Equals(property.Name, "Configuration", StringComparison.OrdinalIgnoreCase))
            {
                configuration = property.Value;
            }
            else if (string.Equals(property.Name, "When", StringComparison.OrdinalIgnoreCase))
            {
                defaultCondition = ReadOptionalString(property.Value, $"{subject} When");
            }
            else if (string.Equals(property.Name, "Phase", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.Number ||
                    !property.Value.TryGetInt32(out phase))
                {
                    throw new JsonException($"{subject} Phase must be a 32-bit integer.");
                }
            }
            else
            {
                ports.Add(property);
            }
        }

        if (type.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(type.GetString()))
            throw new JsonException($"{subject} requires a non-empty Type string.");
        if (phase != 0)
        {
            throw new JsonException(
                $"{subject} uses legacy Phase {phase}. Replace it with an explicit canonical processing profile before migrating.");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        CopyConfiguration(configuration, properties, subject);
        foreach (var port in ports)
        {
            var name = NormalizeName(port.Name, "Port property name");
            if (!properties.TryAdd(
                    name,
                    NormalizeLinkDeclaration(port.Value, workflowName, defaultCondition, $"{subject} port '{name}'")))
            {
                throw new JsonException(
                    $"{subject} uses '{name}' in both Configuration and its port declarations. " +
                    "The canonical flat component shape cannot represent that ambiguity; rename one property before migrating.");
            }
        }

        return new ComponentDefinition(type.GetString()!.Trim(), properties);
    }

    private static void CopyConfiguration(
        JsonElement configuration,
        IDictionary<string, JsonElement> properties,
        string subject)
    {
        if (configuration.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return;

        RequireObject(configuration, $"{subject} Configuration");
        foreach (var property in configuration.EnumerateObject())
        {
            var name = NormalizeName(property.Name, "Configuration property name");
            if (!properties.TryAdd(name, property.Value.Clone()))
            {
                throw new JsonException(
                    $"{subject} Configuration contains duplicate property '{name}' after trimming.");
            }
        }
    }

    private static JsonElement NormalizeLinkDeclaration(
        JsonElement value,
        string workflowName,
        string? defaultCondition,
        string subject)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var items = value.EnumerateArray()
                .Select(item => NormalizeLinkItem(item, workflowName, defaultCondition, subject))
                .ToArray();
            return JsonSerializer.SerializeToElement(items);
        }

        return NormalizeLinkItem(value, workflowName, defaultCondition, subject);
    }

    private static JsonElement NormalizeLinkItem(
        JsonElement value,
        string workflowName,
        string? defaultCondition,
        string subject)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var directSource = NormalizeSource(value.GetString(), workflowName, subject);
            return CreateLink(directSource, defaultCondition);
        }

        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{subject} must be a string, object, or array of either form.");

        var properties = ReadProperties(value, subject, "From", "When");
        if (!properties.TryGetValue("From", out var from) || from.ValueKind != JsonValueKind.String)
            throw new JsonException($"{subject} object requires a string From property.");

        var sourceAddress = NormalizeSource(from.GetString(), workflowName, subject);
        var condition = properties.TryGetValue("When", out var when)
            ? ReadOptionalString(when, $"{subject} When") ?? defaultCondition
            : defaultCondition;
        return CreateLink(sourceAddress, condition);
    }

    private static JsonElement CreateLink(string source, string? condition)
        => condition is null
            ? JsonSerializer.SerializeToElement(source)
            : JsonSerializer.SerializeToElement(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Port"] = source,
                ["Condition"] = condition
            });

    private static string NormalizeSource(string? value, string workflowName, string subject)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException($"{subject} source address cannot be empty.");

        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Any(string.IsNullOrWhiteSpace) || parts.Length is not (2 or 3))
        {
            throw new JsonException(
                $"{subject} source must use 'component.port' or 'workflow.component.port'.");
        }

        if (parts.Length == 3 && string.Equals(parts[0], "Resources", StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException(
                $"{subject} references a legacy executable resource. Register an equivalent canonical host resource instead.");
        }

        return parts.Length == 2
            ? $"{parts[0]}.{parts[1]}"
            : string.Equals(parts[0], workflowName, StringComparison.Ordinal)
                ? $"{parts[1]}.{parts[2]}"
                : $"{parts[0]}.{parts[1]}.{parts[2]}";
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadProperties(
        JsonElement element,
        string subject,
        params string[] allowedNames)
    {
        RequireObject(element, subject);
        var allowed = allowedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                throw new JsonException($"{subject} contains unknown property '{property.Name}'.");
            if (!properties.TryAdd(property.Name, property.Value))
                throw new JsonException($"{subject} contains duplicate property '{property.Name}'.");
        }

        return properties;
    }

    private static string? ReadOptionalString(JsonElement value, string subject)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException($"{subject} must be a string or null.");
        return value.GetString();
    }

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
}
