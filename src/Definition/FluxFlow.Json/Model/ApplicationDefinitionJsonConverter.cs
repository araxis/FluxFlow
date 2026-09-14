using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Json.Model;

internal sealed class ApplicationDefinitionJsonConverter : JsonConverter<ApplicationDefinition>
{
    public override ApplicationDefinition Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        try
        {
            return ReadApplication(document.RootElement);
        }
        catch (JsonException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new JsonException(exception.Message, exception);
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        ApplicationDefinition value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        foreach (var name in value.PropertyOrder ?? new[] { "ExpressionEngine", "Resources", "Workflows" })
        {
            if (name == "ExpressionEngine" && value.ExpressionEngine is not null)
                writer.WriteString(name, value.ExpressionEngine);
            else if (name == "Resources")
            {
                writer.WritePropertyName(name);
                WriteResources(writer, value.Resources);
            }
            else if (name == "Workflows")
            {
                writer.WritePropertyName(name);
                WriteWorkflows(writer, value.Workflows);
            }
        }
        writer.WriteEndObject();
    }

    private static ApplicationDefinition ReadApplication(JsonElement element)
    {
        RequireObject(element, "Application definition");
        JsonElement resources = default;
        JsonElement workflows = default;
        var hasResources = false;
        var hasWorkflows = false;
        string? expressionEngine = null;
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(CanonicalApplicationProperties.Normalize(property.Name)))
                throw new JsonException($"Application definition contains duplicate property '{property.Name}'.");

            switch (CanonicalApplicationProperties.Normalize(property.Name))
            {
                case CanonicalApplicationProperties.ExpressionEngine:
                    expressionEngine = ReadRequiredString(property.Value, "Application ExpressionEngine");
                    break;
                case CanonicalApplicationProperties.Resources:
                    resources = property.Value;
                    hasResources = true;
                    break;
                case CanonicalApplicationProperties.Workflows:
                    workflows = property.Value;
                    hasWorkflows = true;
                    break;
                default:
                    throw new JsonException(
                        $"Application definition supports Resources, Workflows and optional ExpressionEngine; found '{property.Name}'.");
            }
        }

        if (!hasWorkflows)
        {
            throw new JsonException(
                $"Application definition requires '{CanonicalApplicationProperties.Workflows}'.");
        }

        return new ApplicationDefinition(
            hasResources ? ReadResourceMap(resources, CanonicalApplicationProperties.Resources) : [],
            ReadWorkflowMap(workflows), expressionEngine) { PropertyOrder = element.EnumerateObject().Select(p => CanonicalApplicationProperties.Normalize(p.Name)).ToArray() };
    }

    internal static IReadOnlyList<KeyValuePair<string, ResourceDefinition>> ReadResourceMap(
        JsonElement element,
        string path, bool root = true)
    {
        RequireObject(element, $"Resources '{path}'");
        var resources = new List<KeyValuePair<string, ResourceDefinition>>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (root && property.Name.Equals("Functions", StringComparison.OrdinalIgnoreCase))
                throw new JsonException($"{path}.Functions has been removed; use a CallFunction component with RequestTo and ReplyFrom.");
            var name = root && property.Name.Equals("Connections", StringComparison.OrdinalIgnoreCase) ? "Connections" : property.Name;
            if (!names.Add(name))
                throw new JsonException($"Resources '{path}' contains duplicate name '{property.Name}'.");

            resources.Add(new(
                name,
                ReadResource(property.Value, $"{path}.{name}")));
        }

        return resources;
    }

    private static ResourceDefinition ReadResource(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object) return new ResourceDefinition(element);
        var properties = element.EnumerateObject().ToArray();
        EnsureUniqueProperties(properties, $"Resource '{path}'");

        var typeProperties = properties
            .Where(property => string.Equals(property.Name, CanonicalApplicationProperties.Type, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (typeProperties.Length == 0)
            return new ResourceDefinition(JsonSerializer.SerializeToElement(ReadResourceMap(element, path, false).ToDictionary(p => p.Key, p => p.Value.Value)));

        var type = ReadRequiredString(typeProperties[0].Value, $"Resource '{path}' Type");
        return new ResourceInstanceDefinition(
            type,
            properties
                .Where(property => !string.Equals(property.Name, CanonicalApplicationProperties.Type, StringComparison.OrdinalIgnoreCase))
                .Select(property => new KeyValuePair<string, JsonElement>(
                    property.Name,
                    property.Value))) { PropertyOrder = properties.Select(p => p.Name.Equals("Type", StringComparison.OrdinalIgnoreCase) ? "Type" : p.Name).ToArray() };
    }

    private static IReadOnlyList<KeyValuePair<string, WorkflowDefinition>> ReadWorkflowMap(
        JsonElement element)
    {
        RequireObject(element, CanonicalApplicationProperties.Workflows);
        var workflows = new List<KeyValuePair<string, WorkflowDefinition>>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workflow in element.EnumerateObject())
        {
            if (!names.Add(workflow.Name))
                throw new JsonException($"Workflows contains duplicate name '{workflow.Name}'.");

            workflows.Add(new(
                workflow.Name,
                ReadWorkflow(workflow.Value, workflow.Name)));
        }

        return workflows;
    }

    private static WorkflowDefinition ReadWorkflow(JsonElement element, string workflowName)
    {
        RequireObject(element, $"Workflow '{workflowName}'");
        var components = new List<KeyValuePair<string, ComponentDefinition>>();
        IReadOnlyList<KeyValuePair<string, ResourceDefinition>> resources = [];
        string? expressionEngine = null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in element.EnumerateObject())
        {
            if (!names.Add(CanonicalApplicationProperties.NormalizeWorkflowProperty(component.Name)))
            {
                throw new JsonException(
                    $"Workflow '{workflowName}' contains duplicate component '{component.Name}'.");
            }

            if (CanonicalApplicationProperties.Normalize(component.Name) == CanonicalApplicationProperties.ExpressionEngine)
            {
                expressionEngine = ReadRequiredString(component.Value, $"Workflow '{workflowName}' ExpressionEngine");
                continue;
            }

            if (component.Name.Equals("Resources", StringComparison.OrdinalIgnoreCase))
            {
                resources = ReadResourceMap(component.Value, $"{workflowName}.Resources");
                continue;
            }
            components.Add(new(
                component.Name,
                ReadComponent(component.Value, workflowName, component.Name)));
        }

        return new WorkflowDefinition(components, expressionEngine, resources) { PropertyOrder = element.EnumerateObject().Select(p => CanonicalApplicationProperties.NormalizeWorkflowProperty(p.Name)).ToArray() };
    }

    private static ComponentDefinition ReadComponent(
        JsonElement element,
        string workflowName,
        string componentName)
    {
        var subject = $"Component '{workflowName}.{componentName}'";
        RequireObject(element, subject);
        var properties = element.EnumerateObject().ToArray();
        EnsureUniqueProperties(properties, subject, component: true);

        var typePropertyIndex = Array.FindIndex(
            properties,
            static property => string.Equals(
                property.Name,
                CanonicalApplicationProperties.Type,
                StringComparison.OrdinalIgnoreCase));
        if (typePropertyIndex < 0)
            throw new JsonException($"{subject} requires a string '{CanonicalApplicationProperties.Type}' property.");
        var typeProperty = properties[typePropertyIndex];

        return new ComponentDefinition(
            ReadRequiredString(typeProperty.Value, $"{subject} Type"),
            properties
                .Where(property => !string.Equals(property.Name, CanonicalApplicationProperties.Type, StringComparison.OrdinalIgnoreCase))
                .Select(property => new KeyValuePair<string, JsonElement>(
                    property.Name,
                    property.Value))) { PropertyOrder = properties.Select(p => CanonicalApplicationProperties.NormalizeComponentProperty(p.Name)).ToArray() };
    }

    private static void EnsureUniqueProperties(
        IEnumerable<JsonProperty> properties,
        string subject, bool component = false)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var name = component ? CanonicalApplicationProperties.NormalizeComponentProperty(property.Name)
                : property.Name.Equals("Type", StringComparison.OrdinalIgnoreCase) ? "Type" : property.Name;
            if (!names.Add(name))
                throw new JsonException($"{subject} contains duplicate property '{property.Name}'.");
        }
    }

    private static string ReadRequiredString(JsonElement element, string subject)
    {
        if (element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new JsonException($"{subject} must be a non-empty string.");
        }

        return element.GetString()!;
    }

    private static void RequireObject(JsonElement element, string subject)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{subject} must be a JSON object.");
    }

    private static void WriteResources(Utf8JsonWriter writer, IReadOnlyDictionary<string, ResourceDefinition> resources)
    {
        writer.WriteStartObject();
        foreach (var (name, resource) in resources)
        {
            writer.WritePropertyName(name);
            WriteResource(writer, resource);
        }
        writer.WriteEndObject();
    }

    private static void WriteResource(Utf8JsonWriter writer, ResourceDefinition resource)
    {
        if (resource is ResourceInstanceDefinition instance)
        {
            writer.WriteStartObject();
            WriteTypedProperties(writer, instance.Type, instance.Properties, instance.PropertyOrder);
            writer.WriteEndObject();
        }
        else resource.Value.WriteTo(writer);
    }
    private static void WriteWorkflows(Utf8JsonWriter writer, IReadOnlyDictionary<string, WorkflowDefinition> workflows)
    {
        writer.WriteStartObject();
        foreach (var (name, workflow) in workflows)
        {
            writer.WritePropertyName(name);
            writer.WriteStartObject();
            foreach (var property in workflow.PropertyOrder ?? new[] { "ExpressionEngine" }
                .Concat(workflow.Resources.Count == 0 ? [] : new[] { "Resources" }).Concat(workflow.Components.Keys).ToArray())
            {
                if (property == "ExpressionEngine")
                {
                    if (workflow.ExpressionEngine is not null) writer.WriteString(property, workflow.ExpressionEngine);
                    continue;
                }
                if (property == "Resources")
                {
                    writer.WritePropertyName(property);
                    WriteResources(writer, workflow.Resources);
                    continue;
                }
                var component = workflow.Components[property];
                writer.WritePropertyName(property);
                writer.WriteStartObject();
                WriteTypedProperties(writer, component.Type, component.Properties, component.PropertyOrder);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static void WriteTypedProperties(Utf8JsonWriter writer, string type,
        IReadOnlyDictionary<string, JsonElement> properties, IReadOnlyList<string>? order)
    {
        foreach (var name in order ?? new[] { "Type" }.Concat(properties.Keys).ToArray())
        {
            if (name == "Type") writer.WriteString(name, type);
            else
            {
                writer.WritePropertyName(name);
                properties[name].WriteTo(writer);
            }
        }
    }
}
