using System.Collections.Immutable;
using System.Security.Cryptography.X509Certificates;
using FluxFlow.Environments;

namespace FluxFlow.Plugins.Mqtt;

internal static class MqttServiceFields
{
    public static IReadOnlyList<ServiceProperty> For(InfrastructureService service)
    {
        const string connection = "MQTT connection", certificates = "TLS certificates";
        var secure = Uri.TryCreate(service.Endpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme is "mqtts" or "wss";
        var fields = new List<ServiceProperty>
        {
            new(connection, "TLS", "MQTT TLS", secure.ToString(), value => endpoint is null ? service :
                service with { Endpoint = new UriBuilder(endpoint) { Scheme = (endpoint.Scheme is "ws" or "wss", bool.Parse(value)) switch
                    { (true, true) => "wss", (true, false) => "ws", (false, true) => "mqtts", _ => "mqtt" } }.Uri.AbsoluteUri }) { Toggle = true },
            new(connection, "Settings", "MQTT settings source", service.Mqtt is null ? "Use connection" : "Service settings",
                value => service with { Mqtt = value == "Use connection" ? null : service.Mqtt ?? new() })
                { Choices = ["Use connection", "Service settings"] }
        };
        if (service.Mqtt is not { } settings) return fields;

        void Add(string group, string label, string name, string? value, Func<string, MqttServiceSettings> edit,
            bool secret = false, int lines = 1, string[]? choices = null, string? help = null) =>
            fields.Add(new(group, label, "MQTT " + name, value ?? "", text => service with { Mqtt = edit(text) })
                { Secret = secret, Lines = lines, Choices = choices, Help = help });

        Add(connection, "Username", "username", settings.Username, v => settings with { Username = Empty(v), Password = Empty(v) is null ? null : settings.Password });
        Add(connection, "Password", "password", settings.Password, v => settings with { Password = Empty(v) }, secret: true);
        if (!secure) return fields;
        Add(certificates, "Client certificate", "client certificate", settings.ClientCertificate?.Path, v => settings with
            { ClientCertificate = string.IsNullOrWhiteSpace(v) ? null : (settings.ClientCertificate ?? new() { Path = v.Trim() }) with { Path = v.Trim() } });
        if (settings.ClientCertificate is { } cert)
        {
            Add(certificates, "Key file", "private key file", cert.PrivateKeyPath, v => settings with { ClientCertificate = cert with { PrivateKeyPath = Empty(v.Trim()) } });
            Add(certificates, "Key password", "certificate password", cert.Password, v => settings with { ClientCertificate = cert with { Password = Empty(v) } }, secret: true);
        }
        Add(certificates, "Trusted CAs", "trusted certificates", string.Join(Environment.NewLine, settings.TrustedCertificates),
            v => settings with { TrustedCertificates = v.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToImmutableArray() },
            lines: 2, help: "CA certificate paths on the execution host, one per line. Empty uses system trust.");
        Add(certificates, "Revocation", "certificate revocation", settings.CertificateRevocationMode.ToString(),
            v => settings with { CertificateRevocationMode = Enum.Parse<X509RevocationMode>(v) }, choices: Enum.GetNames<X509RevocationMode>());
        return fields;
    }

    private static string? Empty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
