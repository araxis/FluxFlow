using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Environments.Topology;

namespace FluxFlow.Studio.UI.Features.Connections.Effective;

internal sealed record EffectiveConnectionField(string Name, string Value, string Source);
internal sealed record EffectiveConnectionSettings(string Instance, string Source, string? Host,
    IReadOnlyList<EffectiveConnectionField> Fields, string? Notice = null)
{
    internal static IReadOnlyList<EffectiveConnectionSettings> Resolve(ConfigurationResource connection,
        EnvironmentDefinition environment, IReadOnlyList<ConfigurationResource> connections, string endpointProperty,
        ConnectionDescription? description = null, IResourceEnvironmentAdapter? adapter = null)
    {
        var applications = environment.Topology.Nodes.Where(node => node.Kind == EnvironmentNodeKind.Application).ToArray();
        return applications.Length == 0 ? [ForInstance(null)] : applications.Select(ForInstance).ToArray();

        EffectiveConnectionSettings ForInstance(EnvironmentNode? application)
        {
            var instance = application?.Name ?? "Application";
            var original = connection;
            InfrastructureService? existing = null;
            string? host = null;
            var source = "Manual connection";
            var binding = application?.Bindings.FirstOrDefault(pair => pair.Key.Equals(connection.Path, StringComparison.OrdinalIgnoreCase));
            if (binding is { Key: not null } selected)
            {
                var service = environment.Topology.Nodes.FirstOrDefault(node => node.Id == selected.Value && node.Kind == EnvironmentNodeKind.Service);
                if (service is null) return new(instance, "Unavailable binding", null, [], "Choose a service in Environment topology.");
                existing = service.ExistingService;
                original = existing is null ? connections.FirstOrDefault(item => string.Equals(item.Path, service.Resource, StringComparison.OrdinalIgnoreCase))! : connection;
                if (original is null || (existing?.Type ?? original.Type) != connection.Type)
                    return new(instance, "Incompatible binding", null, [], "Choose a compatible service in Environment topology.");
                source = $"Bound service · {service.Name}";
                host = environment.Topology.Nodes.FirstOrDefault(node => node.Id == service.Host)?.Name;
            }
            if (application is not null && TopologyConnections.CheckPlacement(application, connection, environment.Topology, adapter is null ? [] : [adapter]) is { } placement)
                return new(instance, source, host, [], placement.Message);
            var setup = environment.Setup.Resources.FirstOrDefault(pair => pair.Key.Equals(original.Path, StringComparison.OrdinalIgnoreCase)).Value;
            if (existing is null && setup is { Provider: not "existing" })
                return new(instance, $"{source} · {setup.Provider}", host, [], "Endpoint and access settings are assigned at startup.");

            try
            {
                var settings = (JsonObject)original.Settings.DeepClone();
                var manualSource = $"Manual connection · {original.Name}";
                var endpointSource = existing is null ? manualSource : "Bound service";
                if (existing is not null)
                {
                    if (adapter is null) return new(instance, source, host, [], "Install the connection plugin to resolve this service.");
                    existing.Validate(adapter.Infrastructure);
                    adapter.ApplyExisting(settings, existing);
                }
                var overrides = existing is null ? null : adapter?.Infrastructure?.OverriddenSettings?.Invoke(existing);
                var address = existing?.Endpoint ?? ResourceJson.Text(settings, endpointProperty);
                var preview = description?.Preview?.Invoke(settings);
                address = preview?.Endpoint ?? address;
                if (address is not null) address = ConnectionEndpointDisplay.Format(address);
                var fields = new List<EffectiveConnectionField> { new("Endpoint", address ?? "Not set", endpointSource) };
                if (preview != null)
                    fields.AddRange(preview.Fields.Select(f => new EffectiveConnectionField(f.Name, f.Value, f.FromEndpoint ? endpointSource :
                        f.SettingKey is { } key && overrides?.Contains(key) == true ? "Bound service" : manualSource)));
                return new(instance, source, host, fields);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
            {
                return new(instance, source, host, [], "Settings could not be resolved. Review the connection and service definition.");
            }
        }
    }
}
