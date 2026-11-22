using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Configuration.Components.Nats;
using static FluxFlow.Authoring.OptionEditorModel;
using static FluxFlow.Plugins.BrokerConnectionFields;

namespace FluxFlow.Plugins.Nats;

internal static class NatsConnectionProperties
{
    public static PropertyRules Rules { get; } = new()
    {
        Groups = _ => [
            new("Connection", null, [
                Endpoint("nats://localhost:4222", "NATS or TLS endpoint without credentials or a subject path.",
                    address => new NatsConnectionConfiguration { Endpoint = address }.Validate()),
                Create("TimeoutSeconds", "Timeout (s)", OptionEditorKind.Integer, "Connection and command deadline.", 30) with { Min = 1, Max = 600 }]),
            new("Credential variables", null, [
                Variable("UsernameEnvironmentVariable", "Username", "Host variable containing the username; supply a password variable too."),
                Variable("PasswordEnvironmentVariable", "Password", "Host variable containing the password."),
                Variable("TokenEnvironmentVariable", "Token", "Host variable containing a token, instead of username and password."),
                Variable("CredentialsFileEnvironmentVariable", "Credentials file", "Host variable containing the credentials file path, instead of another authentication method.")]),
            new("TLS", null, [
                Create("TlsMode", "Mode", OptionEditorKind.Select, defaultValue: NatsTlsMode.Auto.ToString()) with
                    { Choices = Enum.GetValues<NatsTlsMode>().Select(value => OptionChoiceModel.FromEnum(value)).ToArray() },
                Variable("CaFileEnvironmentVariable", "CA file variable", "Host variable containing the trusted PEM CA file path."),
                Variable("ClientCertificateEnvironmentVariable", "Cert variable", "Host variable containing the PEM client certificate path."),
                Variable("ClientKeyEnvironmentVariable", "Key variable", "Host variable containing the PEM private-key path.")]),
            new("Reconnect", null, [
                Create("MaxReconnectAttempts", "Attempts", OptionEditorKind.Integer, defaultValue: 10) with { Min = 1, Max = 1000 },
                Create("ReconnectWaitSeconds", "Wait (s)", OptionEditorKind.Integer, "Delay between attempts.", 2) with { Min = 1, Max = 600 }])
        ],
        IsVisible = (values, name) => name switch
        {
            "UsernameEnvironmentVariable" or "PasswordEnvironmentVariable" or "TokenEnvironmentVariable" or "CredentialsFileEnvironmentVariable" => AuthFieldVisible(values, name),
            "CaFileEnvironmentVariable" or "ClientCertificateEnvironmentVariable" or "ClientKeyEnvironmentVariable" => Has(values, name) ||
                !Enum.TryParse<NatsTlsMode>(Field(values, "TlsMode").ToString(), true, out var mode) || mode != NatsTlsMode.Disable,
            _ => true
        },
        Validate = settings => NatsConnectionConfiguration.Read(settings)
    };

    private static JsonElement Field(IReadOnlyDictionary<string, JsonElement> values, string name) =>
        values.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool Has(IReadOnlyDictionary<string, JsonElement> values, string name) =>
        Field(values, name).ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    private static bool AuthFieldVisible(IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        if (Has(values, name)) return true; // Keep conflicting saved references reachable for repair.
        bool pair = Has(values, "UsernameEnvironmentVariable") || Has(values, "PasswordEnvironmentVariable");
        bool token = Has(values, "TokenEnvironmentVariable");
        bool file = Has(values, "CredentialsFileEnvironmentVariable");
        return name switch
        {
            "TokenEnvironmentVariable" => !pair && !file,
            "CredentialsFileEnvironmentVariable" => !pair && !token,
            _ => pair || (!file && !token)
        };
    }
}

