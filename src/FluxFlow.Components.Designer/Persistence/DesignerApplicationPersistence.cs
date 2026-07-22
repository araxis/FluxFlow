using System.Text.Json;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using ApplicationWorkflowDefinition = FluxFlow.Composition.Model.WorkflowDefinition;

namespace FluxFlow.Components.Designer.Persistence;

public sealed class DesignerApplicationPersistence
{
    private readonly CompositionNodeRegistry _registry;
    private readonly ComponentDesignMetadataCatalog _metadata;
    private readonly ApplicationLinkCompiler _linkCompiler;
    private readonly ApplicationDefinitionNormalizer _normalizer;

    public DesignerApplicationPersistence(
        CompositionNodeRegistry registry,
        ComponentDesignMetadataCatalog? metadata = null,
        ApplicationLinkCompiler? linkCompiler = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _metadata = metadata ?? new ComponentDesignMetadataCatalog();
        _linkCompiler = linkCompiler ?? new ApplicationLinkCompiler(registry);
        _normalizer = new ApplicationDefinitionNormalizer(registry);
    }

    public DesignerApplicationLoadResult Load(string json)
        => Load(ApplicationDefinitionJson.Deserialize(json));

    public DesignerApplicationLoadResult Load(ReadOnlySpan<byte> utf8Json)
        => Load(ApplicationDefinitionJson.Deserialize(utf8Json));

    public DesignerApplicationLoadResult Load(ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var normalization = _normalizer.Normalize(definition);
        definition = normalization.Definition;

        var compilation = _linkCompiler.Compile(definition);
        var links = new List<DesignerApplicationLink>();
        var resourceAddresses = new HashSet<string>(StringComparer.Ordinal);
        var resources = ProjectResources(definition.Resources, resourceAddresses);
        var workflows = new Dictionary<string, DesignerWorkflow>(StringComparer.Ordinal);
        var references = new List<DesignerResourceReference>();

        foreach (var (workflowName, workflow) in definition.Workflows
                     .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var components = new Dictionary<string, DesignerComponent>(StringComparer.Ordinal);
            foreach (var (componentName, component) in workflow.Components
                         .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                var properties = ProjectComponentProperties(
                    workflowName,
                    componentName,
                    component,
                    links);
                components.Add(componentName, new DesignerComponent
                {
                    Type = component.Type,
                    Properties = properties
                });

                AddResourceReferences(
                    workflowName,
                    componentName,
                    component,
                    resourceAddresses,
                    references);
            }

            workflows.Add(workflowName, new DesignerWorkflow
            {
                Name = workflowName,
                Components = components
            });
        }

        return new DesignerApplicationLoadResult
        {
            Document = new DesignerApplicationDocument
            {
                Resources = resources,
                Workflows = workflows,
                Links = links.ToArray(),
                ResourceReferences = references.ToArray()
            },
            Diagnostics = compilation.Diagnostics,
            NormalizationDiagnostics = normalization.Diagnostics
        };
    }

    public ApplicationDefinition ToDefinition(DesignerApplicationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var componentProperties = new Dictionary<ComponentKey, Dictionary<string, JsonElement>>();
        foreach (var (workflowName, workflow) in document.Workflows)
        {
            if (!string.Equals(workflowName, workflow.Name, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Workflow key '{workflowName}' does not match model name '{workflow.Name}'.",
                    nameof(document));
            }

            foreach (var (componentName, component) in workflow.Components)
            {
                componentProperties.Add(
                    new ComponentKey(workflowName, componentName),
                    new Dictionary<string, JsonElement>(component.Properties, StringComparer.Ordinal));
            }
        }

        var declarations = new Dictionary<DeclarationKey, List<SerializedLinkDeclaration>>();
        foreach (var link in document.Links)
        {
            ArgumentNullException.ThrowIfNull(link);
            var declaredPort = link.DeclarationSide == ApplicationLinkDeclarationSide.Output
                ? link.Source
                : link.Target;
            if (declaredPort.Kind != ApplicationAddressKind.WorkflowPort)
            {
                throw new ArgumentException(
                    $"Link '{link.Source}' to '{link.Target}' cannot be declared on '{declaredPort}'.",
                    nameof(document));
            }

            var componentKey = new ComponentKey(declaredPort.Segments[0], declaredPort.Segments[1]);
            if (!componentProperties.TryGetValue(componentKey, out var properties))
            {
                throw new ArgumentException(
                    $"Link declaration component '{componentKey.Value}' does not exist.",
                    nameof(document));
            }

            var declarationKey = new DeclarationKey(componentKey, declaredPort.Segments[2]);
            if (properties.ContainsKey(declarationKey.Property))
            {
                throw new ArgumentException(
                    $"Component property '{declarationKey.Value}' is already present and cannot also be generated from an editable link.",
                    nameof(document));
            }

            if (!declarations.TryGetValue(declarationKey, out var values))
            {
                values = [];
                declarations.Add(declarationKey, values);
            }

            var reference = link.DeclarationSide == ApplicationLinkDeclarationSide.Output
                ? link.Target
                : link.Source;
            values.Add(new SerializedLinkDeclaration(
                ToPortReference(reference, declaredPort.Segments[0]),
                link.Condition));
        }

        foreach (var (key, values) in declarations)
            componentProperties[key.Component].Add(key.Property, SerializeDeclarations(values));

        var workflows = document.Workflows.ToDictionary(
            static pair => pair.Key,
            pair => new ApplicationWorkflowDefinition(pair.Value.Components.Select(component =>
                new KeyValuePair<string, ComponentDefinition>(
                    component.Key,
                    new ComponentDefinition(
                        component.Value.Type,
                        componentProperties[new ComponentKey(pair.Key, component.Key)])))),
            StringComparer.Ordinal);

        return _normalizer.Normalize(new ApplicationDefinition(
                ToResourceDefinitions(document.Resources),
                workflows))
            .Definition;
    }

    public string Serialize(
        DesignerApplicationDocument document,
        bool writeIndented = false)
        => ApplicationDefinitionJson.Serialize(ToDefinition(document), writeIndented);

    public byte[] SerializeToUtf8Bytes(
        DesignerApplicationDocument document,
        bool writeIndented = false)
        => ApplicationDefinitionJson.SerializeToUtf8Bytes(ToDefinition(document), writeIndented);

    private DesignerResourceNamespace ProjectResources(
        IReadOnlyDictionary<string, ResourceDefinition> resources,
        HashSet<string> addresses)
        => new()
        {
            Path = "Resources",
            Entries = resources.ToDictionary(
                static pair => pair.Key,
                pair => ProjectResource(pair.Value, [pair.Key], addresses),
                StringComparer.Ordinal)
        };

    private static DesignerResourceNode ProjectResource(
        ResourceDefinition definition,
        IReadOnlyList<string> path,
        HashSet<string> addresses)
    {
        if (definition is ResourceInstanceDefinition resource)
        {
            var address = ApplicationAddress.Resource(path.ToArray());
            addresses.Add(address.Value);
            return new DesignerResource
            {
                Address = address,
                Type = resource.Type,
                Properties = resource.Properties
            };
        }

        var group = (ResourceGroupDefinition)definition;
        return new DesignerResourceNamespace
        {
            Path = $"Resources.{string.Join('.', path)}",
            Entries = group.Resources.ToDictionary(
                static pair => pair.Key,
                pair => ProjectResource(pair.Value, [.. path, pair.Key], addresses),
                StringComparer.Ordinal)
        };
    }

    private IReadOnlyDictionary<string, JsonElement> ProjectComponentProperties(
        string workflowName,
        string componentName,
        ComponentDefinition component,
        List<DesignerApplicationLink> links)
    {
        if (!_registry.TryGetRegistration(component.Type, out var registration))
            return component.Properties;

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (propertyName, value) in component.Properties)
        {
            var isInput = registration.Inputs.ContainsKey(propertyName);
            var isOutput = registration.Outputs.ContainsKey(propertyName);
            if (isInput == isOutput ||
                !TryProjectLinks(
                    workflowName,
                    componentName,
                    propertyName,
                    value,
                    isInput ? ApplicationLinkDeclarationSide.Input : ApplicationLinkDeclarationSide.Output,
                    out var projected))
            {
                properties.Add(propertyName, value);
                continue;
            }

            links.AddRange(projected);
        }

        return properties;
    }

    private static bool TryProjectLinks(
        string workflowName,
        string componentName,
        string propertyName,
        JsonElement value,
        ApplicationLinkDeclarationSide side,
        out IReadOnlyList<DesignerApplicationLink> links)
    {
        links = [];
        if (!TryParseDeclarations(value, out var declarations) || declarations.Count == 0)
            return false;

        var declaredPort = ApplicationAddress.WorkflowPort(workflowName, componentName, propertyName);
        var result = new List<DesignerApplicationLink>(declarations.Count);
        foreach (var declaration in declarations)
        {
            if (!ApplicationAddress.TryResolvePort(declaration.Port, workflowName, out var reference))
                return false;

            var source = side == ApplicationLinkDeclarationSide.Input ? reference! : declaredPort;
            var target = side == ApplicationLinkDeclarationSide.Input ? declaredPort : reference!;
            if (source.Kind is not (ApplicationAddressKind.WorkflowPort or ApplicationAddressKind.SystemPort) ||
                target.Kind != ApplicationAddressKind.WorkflowPort ||
                source.Kind == ApplicationAddressKind.SystemPort && side == ApplicationLinkDeclarationSide.Output)
            {
                return false;
            }

            result.Add(new DesignerApplicationLink(source, target, declaration.Condition, side));
        }

        links = result;
        return true;
    }

    private void AddResourceReferences(
        string workflowName,
        string componentName,
        ComponentDefinition component,
        IReadOnlySet<string> resourceAddresses,
        List<DesignerResourceReference> references)
    {
        if (!_metadata.TryGet(new ComponentType(component.Type), out var metadata))
            return;

        var componentAddress = ApplicationAddress.WorkflowComponent(workflowName, componentName);
        foreach (var resource in metadata.Resources.OrderBy(static item => item.Order))
        {
            if (!component.Properties.TryGetValue(resource.Name.Value, out var value))
                continue;

            foreach (var reference in ReadStringValues(value))
            {
                ApplicationAddress? address = null;
                if (ApplicationAddress.TryParse(reference, out var parsed) &&
                    parsed!.Kind == ApplicationAddressKind.Resource)
                {
                    address = parsed;
                }

                references.Add(new DesignerResourceReference
                {
                    Component = componentAddress,
                    PropertyName = resource.Name.Value,
                    Reference = reference,
                    Address = address,
                    IsRequired = resource.IsRequired,
                    Exists = address is not null && resourceAddresses.Contains(address.Value)
                });
            }
        }
    }

    private static IEnumerable<string> ReadStringValues(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            yield return value.GetString()!;
            yield break;
        }

        if (value.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                yield return item.GetString()!;
        }
    }

    private static IReadOnlyDictionary<string, ResourceDefinition> ToResourceDefinitions(
        DesignerResourceNamespace root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!string.Equals(root.Path, "Resources", StringComparison.Ordinal))
            throw new ArgumentException("The root resource namespace path must be 'Resources'.", nameof(root));

        return root.Entries.ToDictionary(
            static pair => pair.Key,
            pair => ToResourceDefinition(pair.Value, [pair.Key]),
            StringComparer.Ordinal);
    }

    private static ResourceDefinition ToResourceDefinition(
        DesignerResourceNode node,
        IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is DesignerResource resource)
        {
            var expectedAddress = ApplicationAddress.Resource(path.ToArray());
            if (resource.Address != expectedAddress)
            {
                throw new ArgumentException(
                    $"Resource model address '{resource.Address}' does not match tree address '{expectedAddress}'.",
                    nameof(node));
            }

            return new ResourceInstanceDefinition(resource.Type, resource.Properties);
        }

        var group = (DesignerResourceNamespace)node;
        var expectedPath = $"Resources.{string.Join('.', path)}";
        if (!string.Equals(group.Path, expectedPath, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Resource namespace path '{group.Path}' does not match tree path '{expectedPath}'.",
                nameof(node));
        }

        return new ResourceGroupDefinition(group.Entries.ToDictionary(
            static pair => pair.Key,
            pair => ToResourceDefinition(pair.Value, [.. path, pair.Key]),
            StringComparer.Ordinal));
    }

    private static bool TryParseDeclarations(
        JsonElement value,
        out IReadOnlyList<ParsedLinkDeclaration> declarations)
    {
        var result = new List<ParsedLinkDeclaration>();
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (!TryParseOne(item, out var declaration))
                {
                    declarations = [];
                    return false;
                }

                result.Add(declaration);
            }
        }
        else
        {
            if (!TryParseOne(value, out var declaration))
            {
                declarations = [];
                return false;
            }

            result.Add(declaration);
        }

        declarations = result;
        return true;
    }

    private static bool TryParseOne(JsonElement value, out ParsedLinkDeclaration declaration)
    {
        declaration = default;
        if (value.ValueKind == JsonValueKind.String)
        {
            var port = value.GetString();
            if (string.IsNullOrWhiteSpace(port))
                return false;

            declaration = new ParsedLinkDeclaration(port, null);
            return true;
        }

        if (value.ValueKind != JsonValueKind.Object)
            return false;

        string? objectPort = null;
        string? condition = null;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name == "Port" && property.Value.ValueKind == JsonValueKind.String)
                objectPort = property.Value.GetString();
            else if (property.Name == "Condition" && property.Value.ValueKind == JsonValueKind.String)
                condition = property.Value.GetString();
            else
                return false;
        }

        if (string.IsNullOrWhiteSpace(objectPort) ||
            condition is not null && string.IsNullOrWhiteSpace(condition))
        {
            return false;
        }

        declaration = new ParsedLinkDeclaration(objectPort, condition);
        return true;
    }

    private static string ToPortReference(ApplicationAddress address, string currentWorkflow)
    {
        if (address.Kind == ApplicationAddressKind.WorkflowPort &&
            string.Equals(address.Segments[0], currentWorkflow, StringComparison.Ordinal))
        {
            return $"{address.Segments[1]}.{address.Segments[2]}";
        }

        return address.Value;
    }

    private static JsonElement SerializeDeclarations(IReadOnlyList<SerializedLinkDeclaration> values)
    {
        if (values.Count == 1)
            return SerializeDeclaration(values[0]);

        return JsonSerializer.SerializeToElement(values.Select(SerializeDeclaration).ToArray());
    }

    private static JsonElement SerializeDeclaration(SerializedLinkDeclaration value)
        => value.Condition is null
            ? JsonSerializer.SerializeToElement(value.Port)
            : JsonSerializer.SerializeToElement(new LinkObject(value.Port, value.Condition));

    private readonly record struct ParsedLinkDeclaration(string Port, string? Condition);

    private readonly record struct SerializedLinkDeclaration(string Port, string? Condition);

    private readonly record struct LinkObject(string Port, string Condition);

    private readonly record struct ComponentKey(string Workflow, string Component)
    {
        public string Value => $"{Workflow}.{Component}";
    }

    private readonly record struct DeclarationKey(ComponentKey Component, string Property)
    {
        public string Value => $"{Component.Value}.{Property}";
    }
}
