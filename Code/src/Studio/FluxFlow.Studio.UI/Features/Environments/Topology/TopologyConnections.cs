using FluxFlow.Authoring;
using FluxFlow.Environments;

namespace FluxFlow.Studio.UI.Features.Environments.Topology;

internal static class TopologyConnections
{
    public static string? ConnectionType(this IReadOnlyList<ConfigurationResource> connections, string path) =>
        connections.FirstOrDefault(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase))?.Type;
    public static bool Supported(string type, IEnumerable<IResourceEnvironmentAdapter>? adapters = null) => InfrastructureServiceTypes.Find(type, adapters) is not null;
    public static string Label(ConfigurationResource resource, IEnumerable<IResourceEnvironmentAdapter>? adapters = null) => $"{resource.Name} · {InfrastructureServiceTypes.Find(resource.Type, adapters)?.Name ?? resource.Type}";
    public static string Icon(string type, IEnumerable<IResourceEnvironmentAdapter> adapters) => InfrastructureServiceTypes.Find(type, adapters)?.Icon ??
        MudBlazor.Icons.Material.Outlined.Language;

    public static string? Validate(EnvironmentTopology topology, IReadOnlyList<ConfigurationResource> connections)
        => Check(topology, connections)?.Message;

    public static EnvironmentCheckResult? CheckPlacement(EnvironmentNode app, ConfigurationResource connection, EnvironmentTopology topology, IEnumerable<IResourceEnvironmentAdapter>? adapters = null)
    {
        if (InfrastructureServiceTypes.Find(connection.Type, adapters)?.RequiresApplicationHost != true) return null;
        var target = topology.BindingTarget(app, connection.Path);
        var service = topology.Nodes.FirstOrDefault(node => node.Id == target);
        var appHost = app.Host ?? topology.Nodes.FirstOrDefault(node => node.Kind == EnvironmentNodeKind.Host && node.LocalHost)?.Id;
        return service is not null && (service.Host is null || service.Host != appHost)
            ? new(false, $"'{service.Name}' must belong to the application's execution host. Remote access to this device is not supported.") { NodeId = service.Id }
            : null;
    }

    public static EnvironmentCheckResult? Check(EnvironmentTopology topology, IReadOnlyList<ConfigurationResource> connections, IEnumerable<IResourceEnvironmentAdapter>? adapters = null)
    {
        foreach (var app in topology.Nodes.Where(node => node.Kind == EnvironmentNodeKind.Application))
        {
            foreach (var binding in app.Bindings)
            {
                var connection = connections.FirstOrDefault(item => item.Path.Equals(binding.Key, StringComparison.OrdinalIgnoreCase));
                var service = topology.Nodes.FirstOrDefault(node => node.Id == binding.Value && node.Kind == EnvironmentNodeKind.Service);
                if (connection is null || !Supported(connection.Type, adapters)) return new(false, $"'{app.Name}' refers to an unavailable connection '{binding.Key}'.") { NodeId = app.Id };
                if (service is null) return new(false, $"'{app.Name}' connection '{connection.Name}' is unbound. Connect it to a compatible service.") { NodeId = app.Id };
                if (EnvironmentTopology.ServiceType(service, connections.ConnectionType) != connection.Type) return new(false, $"'{app.Name}' connection '{connection.Name}' requires {Label(connection, adapters)}; '{service.Name}' is incompatible.") { NodeId = app.Id };
            }
            foreach (var connection in connections)
                if (CheckPlacement(app, connection, topology, adapters) is { } issue) return issue;
        }
        return null;
    }
}
