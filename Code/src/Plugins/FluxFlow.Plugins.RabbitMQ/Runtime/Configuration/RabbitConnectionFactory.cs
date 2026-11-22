using RabbitMQ.Client;

namespace FluxFlow.Configuration.Components.RabbitMQ;

internal static class RabbitConnectionFactory
{
    internal static ConnectionFactory CreateFactory(this RabbitConnectionConfiguration settings)
    {
        settings.Validate();
        var endpoint = new Uri(settings.Endpoint);
        var factory = new ConnectionFactory
        {
            HostName = endpoint.Host, Port = endpoint.IsDefaultPort ? (endpoint.Scheme == "amqps" ? 5671 : 5672) : endpoint.Port,
            VirtualHost = settings.VirtualHost, AutomaticRecoveryEnabled = settings.AutomaticRecovery, TopologyRecoveryEnabled = settings.AutomaticRecovery,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(settings.RecoveryIntervalSeconds),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds), ContinuationTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
        };
        if (endpoint.Scheme == "amqps") factory.Ssl = new() { Enabled = true, ServerName = endpoint.Host };
        if (settings.UsernameEnvironmentVariable is { } user)
        {
            factory.UserName = ReadSecret(user);
            factory.Password = ReadSecret(settings.PasswordEnvironmentVariable!);
        }
        return factory;
    }
    private static string ReadSecret(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Credential environment variable '{name}' is not available to this execution host.");
}
