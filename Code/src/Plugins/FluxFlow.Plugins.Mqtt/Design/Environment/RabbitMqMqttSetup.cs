using FluxFlow.Environments;
using FluxFlow.Plugins.RabbitMQ;

namespace FluxFlow.Plugins.Mqtt;

public static class RabbitMqMqttSetup
{
    public const string Config = "mqtt.listeners.tcp.default = 1883\nmqtt.allow_anonymous = true\nloopback_users.guest = false\n";
    public const string Plugins = "[rabbitmq_mqtt].\n";
    public static EnvironmentProviderDescription Provider { get; } = new("rabbitmq-mqtt", "mqtt.client", "RabbitMQ (MQTT enabled)", "rabbitmq:latest")
    {
        Access = new("Subscribe pattern", "Publish pattern", "RabbitMQ regular expressions on amq.topic routing keys in vhost /. MQTT slashes become dots: ^sensors\\..*$ matches sensors/... . Empty fields deny access. Users can create MQTT subscription queues; anonymous clients are disabled.", 1, "The users above are written to definitions.json and loaded by rabbitmq.conf when the container starts.") { Edits = MqttAccessEdits.Instance },
        StartupTitle = "RabbitMQ startup files",
        StartupFiles =
        [
            new("RabbitMQ configuration", "/etc/rabbitmq/rabbitmq.conf", "Keep MQTT on port 1883. For custom definitions, set definitions.import_backend = local_filesystem and definitions.local.path = /etc/rabbitmq/definitions.json.", _ => Config, ServiceDocumentKind.KeyValueLines, GeneratedWithUsers: true),
            new("Enabled plugins", "/etc/rabbitmq/enabled_plugins", "Erlang list ending with a dot. Keep rabbitmq_mqtt enabled; add plugins already installed in the chosen image.", _ => Plugins, ServiceDocumentKind.NameList)
            { Names = new("Plugin", "Plugins", RabbitBrokerDocuments.PluginNamePattern, "[", ",", "]", ".") { MaxNameLength = RabbitBrokerDocuments.MaxPluginNameLength } },
            new("Definitions", "/etc/rabbitmq/definitions.json", "RabbitMQ definitions for users, vhosts, permissions, queues and exchanges. Enable startup import in rabbitmq.conf.", _ => "{}", ServiceDocumentKind.ObjectCollections, GeneratedWithUsers: true) { Templates = new(RabbitBrokerDocuments.Definitions, RabbitBrokerDocuments.Labels) },
        ]
    };
}
