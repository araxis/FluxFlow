using FluxFlow.Application.Addressing;
using FluxFlow.Authoring;

namespace FluxFlow.Studio.UI.Features.Connections;

public static class ConnectionSelection
{
    public static ConfigurationResource[] Compatible(IEnumerable<ConfigurationResource> connections, string type, string? requiredWorkflow = null)
        => connections.Where(connection => connection.Type == type && (requiredWorkflow is null || connection.Workflow == requiredWorkflow)).ToArray();

    public static ConfigurationResource? Resolve(IEnumerable<ConfigurationResource> connections, string type, string? reference, string workflow)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        try
        {
            return ApplicationAddress.ResolveResourcePaths(reference.Trim(), workflow)
                .Select(path => connections.FirstOrDefault(connection => connection.Type == type && connection.Address == path.Value))
                .FirstOrDefault(connection => connection is not null);
        }
        catch (Exception error) when (error is ArgumentException or FormatException) { return null; }
    }
}
