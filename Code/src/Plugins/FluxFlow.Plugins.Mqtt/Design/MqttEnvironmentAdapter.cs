using System.Text.Json.Nodes;
using FluxFlow.Configuration.Components.Mqtt;

namespace FluxFlow.Environments;

public sealed class MqttEnvironmentAdapter : IResourceEnvironmentAdapter
{
    private static readonly IReadOnlyList<string> SecuritySettings = Array.AsReadOnly(new[] { "Username", "Password", "ClientCertificate", "TrustedCertificates", "CertificateRevocationMode" });

    public void ApplyExisting(JsonObject resource, InfrastructureService service)
    {
        service.Validate(Infrastructure);
        var endpoint = new Uri(service.Endpoint);
        if (service.Mqtt is null) ValidateConnectionCertificates(resource, endpoint);
        ApplyExisting(resource, endpoint);
        if (service.Mqtt is not { } settings) return;
        foreach (var name in SecuritySettings)
            ResourceJson.Remove(resource, name);
        if (settings.Username is not null) ResourceJson.Set(resource, "Username", JsonValue.Create(settings.Username));
        if (settings.Password is not null) ResourceJson.Set(resource, "Password", JsonValue.Create(settings.Password));
        if (settings.ClientCertificate is { } certificate)
        {
            var value = new JsonObject { ["Path"] = certificate.Path };
            if (certificate.Password is not null) value["Password"] = certificate.Password;
            if (certificate.PrivateKeyPath is not null) value["PrivateKeyPath"] = certificate.PrivateKeyPath;
            ResourceJson.Set(resource, "ClientCertificate", value);
        }
        ResourceJson.Set(resource, "TrustedCertificates", new JsonArray(settings.TrustedCertificates.Select(path => (JsonNode?)JsonValue.Create(path)).ToArray()));
        ResourceJson.Set(resource, "CertificateRevocationMode", JsonValue.Create(settings.CertificateRevocationMode.ToString()));
    }

    public void ApplyExisting(JsonObject resource, Uri endpoint)
    {
        ResourceJson.Set(resource, "Address", JsonValue.Create(endpoint.AbsoluteUri));
        foreach (var name in new[] { "Host", "Port", "Tls" }) ResourceJson.Remove(resource, name);
    }
    public void Apply(JsonObject resource, Uri endpoint, ResourceBinding binding)
    {
        Apply(resource, endpoint);
        if (binding.BrokerAccess.GetValueOrDefault(binding.Provider) is not { } access) return;
        access.Validate();
        var user = access.Enabled ? access.Users.Single(item => item.Name == access.ClientUsername)
            : new BrokerUser { Name = access.CustomUsername, Password = access.CustomPassword };
        if (string.IsNullOrEmpty(user.Name)) return;
        ResourceJson.Set(resource, "Username", JsonValue.Create(user.Name));
        ResourceJson.Set(resource, "Password", JsonValue.Create(user.Password));
    }
    public InfrastructureServiceType Infrastructure { get; } = new("mqtt.client", "MQTT broker", "mqtt://broker.example:1883", "MQTT, MQTTS, WS or WSS endpoint without credentials. WebSocket endpoints may include a path and query.", uri => uri.Scheme is "ws" or "wss" || uri.Scheme is "mqtt" or "mqtts" && InfrastructureServiceType.HasNoPath(uri)) {
        ValidateSettings = ValidateSettings,
        OverriddenSettings = service => service.Mqtt is null ? [] : SecuritySettings,
        Fields = FluxFlow.Plugins.Mqtt.MqttServiceFields.For,
        Icon = """<g fill="none" stroke="currentColor" stroke-width="2"><path d="M8 8a6 6 0 0 0 0 8m8-8a6 6 0 0 1 0 8M5 5a10 10 0 0 0 0 14M19 5a10 10 0 0 1 0 14"/></g><circle cx="12" cy="12" r="2"/>"""
    };
    private static void ValidateSettings(InfrastructureService service, Uri endpoint)
    {
        if (service.Mqtt is not { } settings) return;
        if (settings.Password is not null && string.IsNullOrEmpty(settings.Username))
            throw new ArgumentException("MQTT Password requires Username.");
        if (settings.TrustedCertificates.IsDefault || settings.TrustedCertificates.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Trusted certificates require non-empty file paths.");
        if (!Enum.IsDefined(settings.CertificateRevocationMode))
            throw new ArgumentException("Invalid certificate revocation mode.");
        if (settings.ClientCertificate is { } certificate && (string.IsNullOrWhiteSpace(certificate.Path) ||
            certificate.PrivateKeyPath is not null && string.IsNullOrWhiteSpace(certificate.PrivateKeyPath)))
            throw new ArgumentException("Client certificates require non-empty file paths.");
        ValidateCertificateTransport(endpoint, settings.ClientCertificate is not null || settings.TrustedCertificates.Length != 0);
    }

    internal static void ValidateConnectionCertificates(JsonObject resource, Uri endpoint) =>
        ValidateCertificateTransport(endpoint, ResourceJson.Value(resource, "ClientCertificate") is JsonObject { Count: > 0 } ||
            ResourceJson.Value(resource, "TrustedCertificates") is JsonArray { Count: > 0 } or JsonObject { Count: > 0 });

    private static void ValidateCertificateTransport(Uri endpoint, bool hasCertificates)
    {
        if (hasCertificates && endpoint.Scheme is not ("mqtts" or "wss"))
            throw new ArgumentException("MQTT certificates require an mqtts:// or wss:// endpoint.");
    }

    internal static Uri ResolveEndpoint(JsonObject settings)
    {
        var address = ResourceJson.Text(settings, "Address");
        var port = ResourceJson.Value(settings, "Port");
        var explicitTls = ResourceJson.Value(settings, "Tls");
        var uri = MqttClientConfiguration.ResolveAddress(address, ResourceJson.Text(settings, "Host"),
            port is null ? null : int.Parse(port.ToString(), System.Globalization.CultureInfo.InvariantCulture),
            explicitTls is null ? null : bool.Parse(explicitTls.ToString()));
        return address is null && uri.Port == -1
            ? new UriBuilder(uri) { Port = uri.Scheme == "mqtts" ? 8883 : 1883 }.Uri : uri;
    }

    public string Type => "mqtt.client";
    public string? Endpoint(JsonObject resource) => ResourceJson.Text(resource, "Address") ??
        (ResourceJson.Text(resource, "Host") is null ? null : ResolveEndpoint(resource).GetLeftPart(UriPartial.Authority));
    public void Apply(JsonObject resource, Uri endpoint)
    {
        ResourceJson.Set(resource, "Address", JsonValue.Create(endpoint.AbsoluteUri));
        foreach (var name in new[] { "Host", "Port", "Tls", "Username", "Password", "ClientCertificate", "TrustedCertificates" }) ResourceJson.Remove(resource, name);
    }
}
