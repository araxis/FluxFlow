using System.Net;
using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Connections;

namespace FluxFlow.Studio.UI.Features.Environments.Topology;

public sealed record TopologyEndpoint(string Value, string? Warning = null)
{
    internal static TopologyEndpoint Resolve(EnvironmentDefinition definition, EnvironmentNode node,
        ConfigurationResource? resource, PreparedService? running, IEnumerable<IResourceEnvironmentAdapter>? extensions = null)
    {
        var remote = definition.Topology.Nodes.Any(host => host.Kind == EnvironmentNodeKind.Host && host.Id == node.Host && !host.LocalHost);
        if (node.ExistingService is { } existing)
            return Display(existing.Endpoint, remote);
        if (resource is null) return new("Connection unavailable");
        var managed = definition.Setup.Resources.GetValueOrDefault(resource.Path)?.Provider is { } provider && provider != "existing";
        return Read(resource, managed, running, remote, extensions);
    }

    internal static TopologyEndpoint Read(ConfigurationResource resource, IEnumerable<IResourceEnvironmentAdapter>? extensions = null) => Read(resource, false, null, false, extensions);

    private static TopologyEndpoint Read(ConfigurationResource resource, bool managed, PreparedService? running, bool remote, IEnumerable<IResourceEnvironmentAdapter>? extensions)
    {
        var adapter = extensions?.FirstOrDefault(item => item.Type == resource.Type);
        if (adapter is null) return new("Supplied by the component");
        string? address;
        try
        {
            var settings = resource.Settings;
            if (managed)
            {
                if (running is null || !Uri.TryCreate(running.Endpoint, UriKind.Absolute, out var endpoint)) return new("Assigned at startup");
                settings = (JsonObject)settings.DeepClone();
                adapter.Apply(settings, endpoint);
            }
            address = adapter.Endpoint(settings);
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException or ArgumentException)
        {
            return new("Invalid endpoint", "The endpoint could not be read. Review the connection settings.");
        }
        if (string.IsNullOrWhiteSpace(address)) return new("Supplied by the component");
        return Display(address, remote && !managed);
    }

    private static TopologyEndpoint Display(string address, bool remote) =>
        new(ConnectionEndpointDisplay.Format(address, preserveSpelling: true), remote && IsLoopback(address)
            ? "This service is placed on a remote host, but its endpoint uses localhost or a loopback address. It points to the execution computer, not the remote host. Set the connection to a reachable host name or IP address."
            : null);

    private static bool IsLoopback(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;
        if (uri.IsLoopback || uri.Host.TrimEnd('.').Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) &&
            IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);
    }
}
