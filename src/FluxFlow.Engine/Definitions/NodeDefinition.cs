using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Engine.Definitions;

public sealed record NodeDefinition
{
    private Dictionary<string, JsonElement>? _configuration = [];
    private Dictionary<string, JsonElement>? _ports = [];

    public required NodeType Type { get; init; }

    public Dictionary<string, JsonElement> Configuration
    {
        get => _configuration ??= [];
        init => _configuration = value ?? [];
    }

    public string? When { get; init; }
    public int Phase { get; init; } = 0;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Ports
    {
        get => _ports ??= [];
        init => _ports = value ?? [];
    }

    public IReadOnlyList<LinkDefinition> GetPortLinks(string portName, string workflowName)
    {
        if (!Ports.TryGetValue(portName, out var value))
            return [];

        return LinkJson.ParseMany(value, workflowName, When);
    }

    public IReadOnlyDictionary<string, IReadOnlyList<LinkDefinition>> GetAllPortLinks(string workflowName)
        => Ports.ToDictionary(
            port => port.Key,
            port => (IReadOnlyList<LinkDefinition>)LinkJson.ParseMany(port.Value, workflowName, When));
}
