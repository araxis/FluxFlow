namespace FluxFlow.Environments;

public sealed record InfrastructureHost
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "New host";
    public string Description { get; init; } = "";
    public List<InfrastructureService> Services { get; init; } = [];
    public EnvironmentTopology? Topology { get; init; }

    public EnvironmentTopology CreateTopology()
    {
        if (Topology is not null) return new EnvironmentDefinition { Topology = Topology }.Copy().Topology;
        var host = new EnvironmentNode { Kind = EnvironmentNodeKind.Host, Name = Name,
            X = 40, Y = 40, Width = 520, Height = Math.Max(240, 100 + Services.Count * 100) };
        return new() { Nodes = [host, .. Services.Select((service, index) => new EnvironmentNode
        {
            Kind = EnvironmentNodeKind.Service, Name = service.Name, Host = host.Id,
            ExistingService = service with { }, X = 80, Y = 120 + index * 100
        })] };
    }

    public void Validate() => Validate(null);

    public void Validate(IEnumerable<IResourceEnvironmentAdapter>? adapters)
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Services is null || Services.Any(item => item is null))
            throw new ArgumentException("A host requires an identity, name and service list.");
        if (Services.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != Services.Count)
            throw new ArgumentException("Service identifiers must be unique within a host.");
        foreach (var service in Services) service.Validate(InfrastructureServiceTypes.Find(service.Type, adapters));
        if (Topology is { } topology)
        {
            topology.Validate(adapters);
            var host = topology.Nodes.FirstOrDefault(node => node.Kind == EnvironmentNodeKind.Host);
            if (host is null || topology.Nodes.Count(node => node.Kind == EnvironmentNodeKind.Host) != 1 || topology.Nodes.Any(node => node.InfrastructureGroup is not null || node.LocalHost ||
                node.Kind == EnvironmentNodeKind.Application || node.Resource is not null ||
                node != host && node.Host != host.Id))
                throw new ArgumentException("Infrastructure requires one existing host containing its networks and services.");
        }
    }
}

public sealed record InfrastructureService
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "New service";
    public string Type { get; init; } = "mqtt.client";
    public string Endpoint { get; init; } = "mqtt://localhost:1883";
    public MqttServiceSettings? Mqtt { get; init; }

    public void Validate(InfrastructureServiceType? installed = null)
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("A service requires an identity and name.");
        if (string.IsNullOrWhiteSpace(Type) || !Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.Host) || uri.Port == 0 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || installed?.AcceptsEndpoint(uri) == false)
            throw new ArgumentException(installed?.Help ?? $"Unsupported infrastructure service type '{Type}'.");
        if (Mqtt is not null && Type != "mqtt.client")
            throw new ArgumentException("MQTT settings require an MQTT service.");
        installed?.ValidateSettings?.Invoke(this, uri);
    }
}
