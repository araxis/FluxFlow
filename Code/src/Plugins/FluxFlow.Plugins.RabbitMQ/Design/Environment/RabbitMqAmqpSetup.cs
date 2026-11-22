using FluxFlow.Environments;

namespace FluxFlow.Plugins.RabbitMQ;

public static class RabbitMqAmqpSetup
{
    public const string Config = "listeners.tcp.default = 5672\nloopback_users.guest = false\n";
    public static EnvironmentProviderDescription Provider { get; } = new("rabbitmq-amqp", "amqp.connection", "RabbitMQ (AMQP 0.9.1)", "rabbitmq:latest")
    {
        AccessHelp = "Reference credentials available to the execution host. Managed runs use these references instead of the original connection credentials. Leave both empty for the disposable broker's guest account.",
        Variables =
        [
            new("RABBITMQ_DEFAULT_VHOST", "Virtual host", "Virtual host", "/"),
            new("RABBITMQ_DEFAULT_USER_ENVIRONMENT_VARIABLE", "Username variable", "Username environment variable"),
            new("RABBITMQ_DEFAULT_PASS_ENVIRONMENT_VARIABLE", "Password variable", "Password environment variable"),
        ],
        StartupTitle = "RabbitMQ startup files",
        StartupFiles =
        [
            new("RabbitMQ configuration", "/etc/rabbitmq/rabbitmq.conf", "Keep the AMQP listener on port 5672. Import custom definitions with definitions.import_backend = local_filesystem and definitions.local.path = /etc/rabbitmq/definitions.json.", _ => Config, ServiceDocumentKind.KeyValueLines),
            new("Enabled plugins", "/etc/rabbitmq/enabled_plugins", "Erlang list ending with a dot. AMQP does not require the MQTT plugin.", _ => "[].", ServiceDocumentKind.NameList)
            { Names = new("Plugin", "Plugins", RabbitBrokerDocuments.PluginNamePattern, "[", ",", "]", ".") { MaxNameLength = RabbitBrokerDocuments.MaxPluginNameLength } },
            new("Definitions", "/etc/rabbitmq/definitions.json", "Optional users (password hashes), virtual hosts, permissions, queues and exchanges. Enable import in rabbitmq.conf.", _ => "{}", ServiceDocumentKind.ObjectCollections) { Templates = new(RabbitBrokerDocuments.Definitions, RabbitBrokerDocuments.Labels) },
        ]
    };
}
