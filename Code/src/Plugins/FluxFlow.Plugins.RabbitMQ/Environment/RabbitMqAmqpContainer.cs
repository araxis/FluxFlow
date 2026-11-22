using FluxFlow.Environments;

namespace FluxFlow.Plugins.RabbitMQ;

public static class RabbitMqAmqpContainer
{
    public static ContainerServiceDefinition Definition { get; } = new(RabbitMqAmqpSetup.Provider, 5672, null, Validate)
    {
        Scheme = "amqp", Prepare = Prepare, ReadyMessage = "Server startup complete"
    };

    private static void Validate(ResourceBinding binding)
    {
        if (binding.Mappings != "[]" || binding.BrokerAccess.Count != 0)
            throw new ArgumentException("AMQP uses RabbitMQ configuration and definitions; HTTP mappings and MQTT access settings do not apply.");
        if (binding.Variables.ContainsKey("RABBITMQ_DEFAULT_PASS") || binding.Variables.ContainsKey("RABBITMQ_DEFAULT_USER"))
            throw new ArgumentException("Use RABBITMQ_DEFAULT_USER_ENVIRONMENT_VARIABLE and RABBITMQ_DEFAULT_PASS_ENVIRONMENT_VARIABLE to reference credentials on the execution host.");
        if (binding.Variables.TryGetValue("RABBITMQ_DEFAULT_VHOST", out var vhost) && string.IsNullOrWhiteSpace(vhost))
            throw new ArgumentException("The RabbitMQ default virtual host must not be empty.");
        var user = binding.Variables.GetValueOrDefault("RABBITMQ_DEFAULT_USER_ENVIRONMENT_VARIABLE");
        var password = binding.Variables.GetValueOrDefault("RABBITMQ_DEFAULT_PASS_ENVIRONMENT_VARIABLE");
        if ((user is null) != (password is null) || new[] { user, password }.OfType<string>()
            .Any(value => string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['=', '\0']) >= 0))
            throw new ArgumentException("Provide both valid host environment-variable names for the RabbitMQ username and password, or neither.");
    }

    private static ContainerStartup Prepare(ResourceBinding binding)
    {
        var startup = new ContainerStartup { Files = { ["/etc/rabbitmq/rabbitmq.conf"] = RabbitMqAmqpSetup.Config } };
        startup.ResolveVariable(binding, "RABBITMQ_DEFAULT_USER");
        startup.ResolveVariable(binding, "RABBITMQ_DEFAULT_PASS");
        return startup;
    }
}
