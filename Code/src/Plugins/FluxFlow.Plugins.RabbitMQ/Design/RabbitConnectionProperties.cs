using FluxFlow.Authoring;
using FluxFlow.Configuration.Components.RabbitMQ;
using static FluxFlow.Authoring.OptionEditorModel;
using static FluxFlow.Plugins.BrokerConnectionFields;

namespace FluxFlow.Plugins.RabbitMQ;

internal static class RabbitConnectionProperties
{
    public static PropertyRules Rules { get; } = new()
    {
        Groups = _ => [
            new("Connection", null, [
                Endpoint("amqp://localhost:5672", "AMQP or AMQPS endpoint without credentials or a virtual-host path.",
                    address => new RabbitConnectionConfiguration { Endpoint = address }.Validate()),
                Create("VirtualHost", "Virtual host", defaultValue: "/"),
                Create("TimeoutSeconds", "Timeout (s)", OptionEditorKind.Integer, "Connection and channel-operation deadline.", 30) with { Min = 1, Max = 600 }]),
            new("Credential variables", null, [
                Variable("UsernameEnvironmentVariable", "Username", "Host variable containing the username. Leave both references unset for guest access."),
                Variable("PasswordEnvironmentVariable", "Password", "Host variable containing the password.")]),
            new("Recovery", null, [
                Create("AutomaticRecovery", "Auto recover", OptionEditorKind.Toggle, "Reconnect and restore subscriptions.", true),
                Create("RecoveryIntervalSeconds", "Interval (s)", OptionEditorKind.Integer, "Delay between recovery attempts.", 5) with { Min = 1, Max = 600 }])
        ],
        IsVisible = (values, name) => name != "RecoveryIntervalSeconds" ||
            !bool.TryParse(values.FirstOrDefault(p => p.Key.Equals("AutomaticRecovery", StringComparison.OrdinalIgnoreCase)).Value.ToString(), out var enabled) || enabled,
        Validate = settings => RabbitConnectionConfiguration.Read(settings)
    };
}
