using FluxFlow.Authoring;
using FluxFlow.Environments;

namespace FluxFlow.Studio.UI.Features.Connections;

internal static class DiscoveryEndpoints
{
    public static IReadOnlyList<string> Resolve(IReadOnlyList<ConfigurationResource> resources,
        EnvironmentDefinition environment, string reference, string workflow, string type)
    {
        var resource = ConnectionSelection.Resolve(resources, type, reference, workflow)
            ?? throw new ArgumentException("The connection is unavailable.");
        var targets = new List<string>();
        var applications = environment.Topology.Nodes.Where(node => node.Kind == EnvironmentNodeKind.Application).ToArray();
        foreach (var application in applications)
        {
            RequireLocal(application);
            var binding = application.Bindings.FirstOrDefault(pair => pair.Key.Equals(resource.Path, StringComparison.OrdinalIgnoreCase));
            var service = binding.Key is null
                ? Placement()
                : environment.Topology.Nodes.FirstOrDefault(node => node.Kind == EnvironmentNodeKind.Service && node.Id == binding.Value)
                    ?? throw new ArgumentException("The bound service is unavailable.");
            if (service is not null) RequireLocal(service);
            targets.Add(Resolve(service));
        }
        if (applications.Length == 0)
        {
            var service = Placement();
            if (service is not null) RequireLocal(service);
            targets.Add(Resolve(service));
        }
        return targets;

        EnvironmentNode? Placement() => environment.Topology.Nodes.FirstOrDefault(node => node.Kind == EnvironmentNodeKind.Service &&
            string.Equals(node.Resource, resource.Path, StringComparison.OrdinalIgnoreCase));

        void RequireLocal(EnvironmentNode node)
        {
            if (node.Host is { } host && !environment.Topology.Nodes.Any(item => item.Id == host && item.LocalHost))
                throw new ArgumentException("Discovery on a remote host is unavailable. Choose a connection on the local companion.");
        }
        string Resolve(EnvironmentNode? service)
        {
            var source = service?.Resource is { } path
                ? resources.FirstOrDefault(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException("The bound connection is unavailable.")
                : resource;
            if (source.Type != type || service?.ExistingService is { } snapshot && snapshot.Type != type)
                throw new ArgumentException("The bound service has an incompatible connection type.");
            var endpoint = service?.ExistingService?.Endpoint ?? ResourceJson.Text(source.Settings, "Endpoint");
            return endpoint ?? "";
        }
    }
}
