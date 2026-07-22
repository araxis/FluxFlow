using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Composition.Model;

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
        writer.WritePropertyName(CanonicalApplicationProperties.Resources);
        WriteResources(writer, value.Resources);
        writer.WritePropertyName(CanonicalApplicationProperties.Workflows);
        WriteWorkflows(writer, value.Workflows);
        writer.WriteEndObject();
    }

    private static ApplicationDefinition ReadApplication(JsonElement element)
    {
        RequireObject(element, "Application definition");
        JsonElement resources = default;
        JsonElement workflows = default;
        var hasResources = false;
        var hasWorkflows = false;
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new JsonException($"Application definition contains duplicate property '{property.Name}'.");

            switch (property.Name)
            {
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
                        $"Application definition supports only '{CanonicalApplicationProperties.Resources}' and '{CanonicalApplicationProperties.Workflows}'; found '{property.Name}'.");
            }
        }

        if (!hasResources || !hasWorkflows)
        {
            throw new JsonException(
                $"Application definition requires exactly '{CanonicalApplicationProperties.Resources}' and '{CanonicalApplicationProperties.Workflows}'.");
        }

        return new ApplicationDefinition(
            ReadResourceMap(resources, CanonicalApplicationProperties.Resources),
            ReadWorkflowMap(workflows));
    }

    private static IReadOnlyList<KeyValuePair<string, ResourceDefinition>> ReadResourceMap(
        JsonElement element,
        string path)
    {
        RequireObject(element, $"Resource group '{path}'");
        var resources = new List<KeyValuePair<string, ResourceDefinition>>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new JsonException($"Resource group '{path}' contains duplicate name '{property.Name}'.");

            resources.Add(new(
                property.Name,
                ReadResource(property.Value, $"{path}.{property.Name}")));
        }

        return resources;
    }

    private static ResourceDefinition ReadResource(JsonElement element, string path)
    {
        RequireObject(element, $"Resource '{path}'");
        var properties = element.EnumerateObject().ToArray();
        EnsureUniqueProperties(properties, $"Resource '{path}'");

        var typeProperties = properties
            .Where(property => string.Equals(property.Name, CanonicalApplicationProperties.Type, StringComparison.Ordinal))
            .ToArray();
        if (typeProperties.Length == 0)
            return new ResourceGroupDefinition(ReadResourceMap(element, path));

        var type = ReadRequiredString(typeProperties[0].Value, $"Resource '{path}' Type");
        return new ResourceInstanceDefinition(
            type,
            properties
                .Where(property => !string.Equals(property.Name, CanonicalApplicationProperties.Type, StringComparison.Ordinal))
                .Select(property => new KeyValuePair<string, JsonElement>(
                    property.Name,
                    property.Value)));
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
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in element.EnumerateObject())
        {
            if (!names.Add(component.Name))
            {
                throw new JsonException(
                    $"Workflow '{workflowName}' contains duplicate component '{component.Name}'.");
            }

            components.Add(new(
                component.Name,
                ReadComponent(component.Value, workflowName, component.Name)));
        }

        return new WorkflowDefinition(components);
    }

    private static ComponentDefinition ReadComponent(
        JsonElement element,
        string workflowName,
        string componentName)
    {
        var subject = $"Component '{workflowName}.{componentName}'";
        RequireObject(element, subject);
        var properties = element.EnumerateObject().ToArray();
        EnsureUniqueProperties(properties, subject);

        var typeProperty = properties.SingleOrDefault(
            property => string.Equals(property.Name, CanonicalApplicationProperties.Type, StringComparison.Ordinal));
        if (typeProperty.Name is null)
            throw new JsonException($"{subject} requires a string '{CanonicalApplicationProperties.Type}' property.");

        return new ComponentDefinition(
            ReadRequiredString(typeProperty.Value, $"{subject} Type"),
            properties
                .Where(property => !string.Equals(property.Name, CanonicalApplicationProperties.Type, StringComparison.Ordinal))
                .Select(property => new KeyValuePair<string, JsonElement>(
                    property.Name,
                    property.Value)));
    }

    private static void EnsureUniqueProperties(
        IEnumerable<JsonProperty> properties,
        string subject)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (!names.Add(property.Name))
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

    private static void WriteResources(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, ResourceDefinition> resources)
    {
        writer.WriteStartObject();
        foreach (var (name, resource) in resources.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(name);
            WriteResource(writer, resource);
        }
        writer.WriteEndObject();
    }

    private static void WriteResource(Utf8JsonWriter writer, ResourceDefinition resource)
    {
        writer.WriteStartObject();
        switch (resource)
        {
            case ResourceGroupDefinition group:
                foreach (var (name, child) in group.Resources.OrderBy(
                             item => item.Key,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(name);
                    WriteResource(writer, child);
                }
                break;
            case ResourceInstanceDefinition instance:
                writer.WriteString(CanonicalApplicationProperties.Type, instance.Type);
                WriteProperties(writer, instance.Properties);
                break;
            default:
                throw new JsonException($"Unsupported resource definition type '{resource.GetType().Name}'.");
        }
        writer.WriteEndObject();
    }

    private static void WriteWorkflows(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, WorkflowDefinition> workflows)
    {
        writer.WriteStartObject();
        foreach (var (workflowName, workflow) in workflows.OrderBy(
                     item => item.Key,
                     StringComparer.Ordinal))
        {
            writer.WritePropertyName(workflowName);
            writer.WriteStartObject();
            foreach (var (componentName, component) in workflow.Components.OrderBy(
                         item => item.Key,
                         StringComparer.Ordinal))
            {
                writer.WritePropertyName(componentName);
                writer.WriteStartObject();
                writer.WriteString(CanonicalApplicationProperties.Type, component.Type);
                WriteProperties(writer, component.Properties);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static void WriteProperties(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, JsonElement> properties)
    {
        foreach (var (name, value) in properties.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(name);
            WriteJsonValue(writer, value);
        }
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(
                         property => property.Name,
                         StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteJsonValue(writer, property.Value);
            }
            writer.WriteEndObject();
            return;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
                WriteJsonValue(writer, item);
            writer.WriteEndArray();
            return;
        }

        value.WriteTo(writer);
    }
}
