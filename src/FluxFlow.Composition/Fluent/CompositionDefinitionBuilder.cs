using System.Text.Json;

namespace FluxFlow.Composition;

public sealed class CompositionDefinitionBuilder
{
    private readonly Dictionary<string, WorkflowDefinition> _workflows =
        new(StringComparer.Ordinal);

    private CompositionDefinitionBuilder()
    {
    }

    public static CompositionDefinitionBuilder Create() => new();

    public CompositionDefinitionBuilder Workflow(
        string name,
        Action<WorkflowDefinitionBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var normalizedName = name.Trim();
        if (_workflows.ContainsKey(normalizedName))
            throw new InvalidOperationException($"Workflow '{name}' is already defined.");

        var builder = new WorkflowDefinitionBuilder(normalizedName);
        configure(builder);
        _workflows.Add(normalizedName, builder.Build());
        return this;
    }

    public CompositionDefinition Build()
        => new()
        {
            Workflows = _workflows.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal)
        };
}

public sealed class WorkflowDefinitionBuilder
{
    private readonly Dictionary<string, NodeDefinition> _nodes =
        new(StringComparer.Ordinal);
    private readonly List<LinkDefinition> _links = [];

    internal WorkflowDefinitionBuilder(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public WorkflowDefinitionBuilder Node(
        string name,
        string type,
        Action<NodeDefinitionBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var normalizedName = name.Trim();
        if (_nodes.ContainsKey(normalizedName))
            throw new InvalidOperationException($"Node '{Name}.{name}' is already defined.");

        var builder = new NodeDefinitionBuilder(type);
        configure?.Invoke(builder);
        _nodes.Add(normalizedName, builder.Build());
        return this;
    }

    public WorkflowDefinitionBuilder Link(string from, string to)
    {
        _links.Add(new LinkDefinition
        {
            From = PortReference.Parse(from),
            To = PortReference.Parse(to)
        });
        return this;
    }

    public WorkflowDefinitionBuilder Link(PortReference from, PortReference to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        _links.Add(new LinkDefinition { From = from, To = to });
        return this;
    }

    internal WorkflowDefinition Build()
        => new()
        {
            Nodes = _nodes.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal),
            Links = _links.ToList()
        };
}

public sealed class NodeDefinitionBuilder
{
    private readonly Dictionary<string, JsonElement> _configuration =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _resources =
        new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _serializerOptions;

    internal NodeDefinitionBuilder(string type)
    {
        Type = type.Trim();
        _serializerOptions = CompositionDefinitionJson.CreateSerializerOptions();
    }

    public string Type { get; }

    public NodeDefinitionBuilder Configure<TValue>(string name, TValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _configuration[name.Trim()] = JsonSerializer.SerializeToElement(value, _serializerOptions);
        return this;
    }

    public NodeDefinitionBuilder Resource(string name, string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        _resources[name.Trim()] = reference;
        return this;
    }

    internal NodeDefinition Build()
        => new()
        {
            Type = Type,
            Configuration = _configuration.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal),
            Resources = _resources.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal)
        };
}
