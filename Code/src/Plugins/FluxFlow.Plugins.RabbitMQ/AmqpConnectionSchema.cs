using System.Text.Json;

namespace FluxFlow.Plugins.RabbitMQ;

internal static class AmqpConnectionSchema
{
    internal static PluginConnection Describe()
    {
        using var defaults = JsonDocument.Parse("""{"Type":"amqp.connection","Endpoint":"amqp://localhost:5672","VirtualHost":"/","AutomaticRecovery":true}""");
        return new("amqp.connection", "RabbitMQ (AMQP)", "Endpoint", false, defaults.RootElement.Clone(), []);
    }
}
