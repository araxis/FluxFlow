using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Environments;

public sealed record EnvironmentDefinition
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Local development";
    public EnvironmentProfile Setup { get; set; } = new();
    public EnvironmentTopology Topology { get; set; } = new();

    public static string ValidateNewName(string? name, IEnumerable<EnvironmentDefinition> environments) =>
        ValidateName(name, environments, null);

    private static string ValidateName(string? name, IEnumerable<EnvironmentDefinition> environments, string? exceptId)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || environments.Any(item => item.Id != exceptId &&
            string.Equals(item.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose a unique environment name.");
        return trimmed;
    }

    public static EnvironmentDefinition Create(string name, IEnumerable<EnvironmentDefinition> environments) =>
        new() { Name = ValidateNewName(name, environments) };

    public EnvironmentDefinition Rename(string name, IEnumerable<EnvironmentDefinition> environments)
    {
        // A saved name may predate trimming. Submitting it verbatim is not an edit.
        if (name == Name) return this;
        var trimmed = ValidateName(name, environments, Id);
        return Name == trimmed ? this : this with { Name = trimmed };
    }

    public EnvironmentDefinition Copy() => JsonSerializer.Deserialize(
        JsonSerializer.Serialize(this, EnvironmentJsonContext.Default.EnvironmentDefinition),
        EnvironmentJsonContext.Default.EnvironmentDefinition)! with { Setup = Setup.Copy() };

    public void Validate() => Validate(null);

    public void Validate(IEnumerable<IResourceEnvironmentAdapter>? adapters)
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Setup is null || Topology is null)
            throw new ArgumentException("An environment requires an identity, name, setup and topology.");
        Setup.Validate();
        Topology.Validate(adapters);
    }
}

public sealed partial record EnvironmentTopology
{
    public List<EnvironmentNode> Nodes { get; init; } = [];

    public bool SetColor(string nodeId, string? color)
    {
        var node = Nodes.Find(item => item.Id == nodeId);
        if (node is null || node.InfrastructureGroup is not null) return false;
        ValidateColor(color);
        if (string.Equals(node.Color, color, StringComparison.OrdinalIgnoreCase)) return false;
        node.Color = color;
        return true;
    }

    private static void ValidateColor(string? color)
    {
        if (color is not null && (color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit)))
            throw new ArgumentException("Topology colors must use #RRGGBB format.");
    }

    public void Validate() => Validate(null);

    public void Validate(IEnumerable<IResourceEnvironmentAdapter>? adapters)
    {
        if (Nodes is null || Nodes.Any(node => node is null || string.IsNullOrWhiteSpace(node.Id) ||
            string.IsNullOrWhiteSpace(node.Name) || !Enum.IsDefined(node.Kind) ||
            !double.IsFinite(node.X) || !double.IsFinite(node.Y) || node.Networks is null || node.Bindings is null ||
            node.Width is { } width && (!double.IsFinite(width) || width < 240) ||
            node.Height is { } height && (!double.IsFinite(height) || height < 180)))
            throw new ArgumentException("Topology nodes require valid identities, names, kinds and positions.");
        if (Nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != Nodes.Count ||
            Nodes.Count(node => node.LocalHost) > 1)
            throw new ArgumentException("Topology identities must be unique, with at most one local host.");
        if (Nodes.Where(node => node.Kind == EnvironmentNodeKind.Service && node.Resource is not null).Select(node => node.Resource)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != Nodes.Count(node => node.Kind == EnvironmentNodeKind.Service && node.Resource is not null))
            throw new ArgumentException("Place each connection only once in an environment.");
        foreach (var node in Nodes)
        {
            if (node.PlacementScale is { } scale &&
                (!double.IsFinite(scale) || scale < .2 || scale > 4 || node.InfrastructureGroup != node.Id || node.Kind != EnvironmentNodeKind.Host))
                throw new ArgumentException("Saved host placement scale must be between 0.2 and 4.");
            if (node.InfrastructureGroup is { } group &&
                (!Nodes.Any(root => root.Id == group && root.Kind == EnvironmentNodeKind.Host && root.InfrastructureGroup == group) ||
                 node.Id != group && node.Host != group || node.Kind == EnvironmentNodeKind.Application || node.Resource is not null))
                throw new ArgumentException("Infrastructure snapshots require their host and existing services.");
            if (node.Kind != EnvironmentNodeKind.Application && node.Host is { } parent && Nodes.Any(host => host.Id == parent && host.InfrastructureGroup is not null && host.InfrastructureGroup != node.InfrastructureGroup) ||
                node.Networks.Any(id => Nodes.Any(network => network.Id == id && network.InfrastructureGroup is not null && network.InfrastructureGroup != node.InfrastructureGroup &&
                    (node.Kind != EnvironmentNodeKind.Application || node.Host != network.InfrastructureGroup))))
                throw new ArgumentException("Saved host service definitions are read-only. Applications may use the host and its networks; bind their connections to its services.");
            if (node.ExistingService is { } service)
                service.Validate(InfrastructureServiceTypes.Find(service.Type, adapters));
            if (node.ExistingService is not null && (node.Kind != EnvironmentNodeKind.Service || node.Resource is not null))
                throw new ArgumentException("An infrastructure service must be independent of project connection paths.");
            if (node.Bindings.Count > 0 && node.Kind != EnvironmentNodeKind.Application ||
                node.Bindings.Keys.Any(string.IsNullOrWhiteSpace) ||
                node.Bindings.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != node.Bindings.Count)
                throw new ArgumentException("Only application instances can bind distinct connection paths.");
            ValidateColor(node.Color);
            if (node.LocalHost && node.Kind != EnvironmentNodeKind.Host ||
                node.Kind == EnvironmentNodeKind.Service && node.ExistingService is null && string.IsNullOrWhiteSpace(node.Resource))
                throw new ArgumentException("Only hosts can be local; services require a connection path.");
            if (node.Host is not null && (node.Kind == EnvironmentNodeKind.Host ||
                !Nodes.Any(host => host.Id == node.Host && host.Kind == EnvironmentNodeKind.Host)))
                throw new ArgumentException("Placement must refer to an existing host.");
            if (node.Networks.Count > 0 && node.Kind == EnvironmentNodeKind.Network ||
                node.Networks.Distinct(StringComparer.Ordinal).Count() != node.Networks.Count ||
                node.Networks.Any(id => !Nodes.Any(network => network.Id == id && network.Kind == EnvironmentNodeKind.Network)))
                throw new ArgumentException("Network attachments must refer to distinct network nodes.");
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<EnvironmentNodeKind>))]
public enum EnvironmentNodeKind { Application, Host, Network, Service }

public sealed record EnvironmentNode
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public EnvironmentNodeKind Kind { get; init; }
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Width { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Height { get; set; }
    public string? Resource { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InfrastructureService? ExistingService { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InfrastructureGroup { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PlacementScale { get; set; }
    public string? Host { get; set; }
    public bool LocalHost { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool KeepInsideContainer { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Color { get; set; }
    public Dictionary<string, string?> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Networks { get; init; } = [];
}
