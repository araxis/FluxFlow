using FluxFlow.Environments;

namespace FluxFlow.Plugins.Mqtt;

public static class RabbitMqMqttContainer
{
    public static ContainerServiceDefinition Definition { get; } = new(RabbitMqMqttSetup.Provider, 1883, null, Validate)
    {
        Scheme = "mqtt", Prepare = Prepare, ReadyMessage = "Server startup complete"
    };

    private static void Validate(ResourceBinding binding)
    {
        RabbitMqAccess.Validate(binding);
        if (binding.Mappings != "[]") throw new ArgumentException("MQTT does not accept HTTP mappings.");
    }

    private static ContainerStartup Prepare(ResourceBinding binding)
    {
        var files = binding.BrokerAccess.GetValueOrDefault(Definition.Provider.Name) is { Enabled: true } access
            ? RabbitMqAccess.Files(access) : new() { [RabbitMqAccess.Config] = RabbitMqMqttSetup.Config };
        files["/etc/rabbitmq/enabled_plugins"] = RabbitMqMqttSetup.Plugins;
        return new() { Files = files };
    }
}
