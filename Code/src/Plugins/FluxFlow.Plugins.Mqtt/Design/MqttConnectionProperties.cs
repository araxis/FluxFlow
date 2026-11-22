using FluxFlow.Authoring;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Environments;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using Polly;
using static FluxFlow.Authoring.OptionEditorModel;
using Microsoft.Extensions.Configuration;
using FluxFlow.Plugins;

namespace FluxFlow.Plugins.Mqtt;

internal static class MqttConnectionProperties
{
    internal static PropertyRules Rules => new()
    {
        Preview = Preview,
        Initialize = settings => settings["ClientId"] = $"fluxflow-{Guid.NewGuid():N}",
        Groups = settings => [
            new("Target", null, ResourceJson.Value(settings, "Address") is null && ResourceJson.Value(settings, "Host") is not null
                ? [Create("Host", "Host") with { IsRequired = true, ValidateText = host => CheckAddress(null, host) }, Create("Port", "Port", OptionEditorKind.Integer) with { Min = 1, Max = 65535 },
                    Create("Tls", "TLS", OptionEditorKind.Toggle, defaultValue: false)]
                : [Create("Address", "Address", help: "mqtt, mqtts, tcp, ws or wss endpoint. TLS follows the address scheme.") with
                    { IsRequired = true, ValidateText = address => CheckAddress(address, null) },
                    .. (ResourceJson.Value(settings, "Tls") is null ? Array.Empty<OptionEditorModel>() : [Create("Tls", "TLS", OptionEditorKind.Toggle, "Explicit TLS setting; must agree with the address scheme.")])]),
            new("Authentication", null, [Create("Username", "Username") with { AllowEmptyText = true }, Create("Password", "Password", OptionEditorKind.Secret) with { AllowEmptyText = true }]),
            new("TLS", null, [EnumSelect("CertificateRevocationMode", "Revocation", X509RevocationMode.Online)])
                { ShowGroup = values => UsesTls(values) || Has(values, "CertificateRevocationMode") || Has(values, "TrustedCertificates"),
                  StringLists = [new("TrustedCertificates", "Trusted CAs", "CA file", path => string.IsNullOrWhiteSpace(path) ? "Enter a CA file path." : null)] },
            new("Client certificate", "ClientCertificate", [Create("Path", "File"), Create("PrivateKeyPath", "Key file"), Create("Password", "Key password", OptionEditorKind.Secret) with { AllowEmptyText = true }])
                { ShowGroup = values => UsesTls(values) || Has(values, "ClientCertificate") },
            new("Client", null, [Create("ClientId", "Client ID") with { IsRequired = true }, Create("AutoConnect", "Auto connect", OptionEditorKind.Toggle, defaultValue: true),
                Create("CleanSession", "Clean session", OptionEditorKind.Toggle, defaultValue: true),
                Create("SessionExpiryInterval", "Session expiry", OptionEditorKind.Integer, "Seconds.", 0) with { Min = 0, Max = uint.MaxValue },
                Create("Timeout", "Timeout", OptionEditorKind.Duration, defaultValue: "00:00:10") with
                    { MinDuration = TimeSpan.FromTicks(1), MaxDuration = TimeSpan.FromMilliseconds(uint.MaxValue - 1) }]),
            new("Reconnect", "Reconnect", [Create("MaxRetryAttempts", "Attempts", OptionEditorKind.Integer, "Zero disables reconnect.", int.MaxValue) with { Min = 0, Max = int.MaxValue },
                Create("Delay", "Delay", OptionEditorKind.Duration, defaultValue: "00:00:05") with { MinDuration = TimeSpan.Zero, MaxDuration = TimeSpan.FromDays(1) },
                EnumSelect("BackoffType", "Backoff", DelayBackoffType.Constant),
                Create("UseJitter", "Jitter", OptionEditorKind.Toggle, defaultValue: false), Create("MaxDelay", "Max delay", OptionEditorKind.Duration) with { MinDuration = TimeSpan.Zero, MaxDuration = TimeSpan.FromDays(1) }])
        ],
        Validate = settings => (settings.Get<MqttClientConfiguration>() ?? throw new ArgumentException("Missing MQTT settings.")).Validate(settings)
    };

    private static ConnectionPreview Preview(System.Text.Json.Nodes.JsonObject settings)
    {
        var uri = MqttEnvironmentAdapter.ResolveEndpoint(settings);
        var tls = uri.Scheme is "mqtts" or "wss";
        MqttEnvironmentAdapter.ValidateConnectionCertificates(settings, uri);
        var certificate = ResourceJson.Value(settings, "ClientCertificate") as System.Text.Json.Nodes.JsonObject;
        var trust = ResourceJson.Value(settings, "TrustedCertificates") as System.Text.Json.Nodes.JsonArray;
        return new(uri.AbsoluteUri, [
            new("TLS", tls ? "Enabled" : "Disabled", FromEndpoint: true),
            new("Username", ResourceJson.Text(settings, "Username") ?? "Not set") { SettingKey = "Username" },
            new("Password", ResourceJson.Value(settings, "Password") is null ? "Not set" : "Configured") { SettingKey = "Password" },
            new("Client certificate", certificate is null ? "Not set" : ResourceJson.Text(certificate, "Path") ?? "Not set") { SettingKey = "ClientCertificate" },
            new("Trusted CAs", trust is null || trust.Count == 0 ? "System trust" : string.Join(", ", trust.Select(item => item?.GetValue<string>()))) { SettingKey = "TrustedCertificates" },
            new("Revocation", ResourceJson.Value(settings, "CertificateRevocationMode")?.ToString() ?? "Online") { SettingKey = "CertificateRevocationMode" }
        ]);
    }
    private static JsonElement Field(IReadOnlyDictionary<string, JsonElement> values, string name) =>
        values.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    private static OptionEditorModel EnumSelect<T>(string name, string label, T fallback) where T : struct, Enum =>
        Create(name, label, OptionEditorKind.Select, defaultValue: fallback.ToString()) with
        { Choices = Enum.GetValues<T>().Select(value => OptionChoiceModel.FromEnum(value)).ToArray() };

    private static string? CheckAddress(string? address, string? host)
    {
        try { _ = MqttClientConfiguration.ResolveAddress(address, host); return null; }
        catch (Exception error) when (error is ArgumentException or FormatException) { return error.Message; }
    }

    private static bool Has(IReadOnlyDictionary<string, JsonElement> values, string name) =>
        Field(values, name).ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    private static bool UsesTls(IReadOnlyDictionary<string, JsonElement> values) =>
        Field(values, "Address") is { ValueKind: JsonValueKind.String } address
            ? Uri.TryCreate(address.GetString(), UriKind.Absolute, out var uri) && uri.Scheme is "mqtts" or "wss"
            : Field(values, "Tls") is { ValueKind: JsonValueKind.True or JsonValueKind.String } tls && bool.TryParse(tls.ToString(), out var enabled) && enabled;
}
