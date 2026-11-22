using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

public sealed class RabbitMqEnvironmentAdapter : IResourceEnvironmentAdapter
{
    public InfrastructureServiceType Infrastructure { get; } = new("amqp.connection", "RabbitMQ (AMQP)", "amqp://broker.example:5672", "AMQP 0.9.1 over AMQP or AMQPS. Set the virtual host and credential references on the project connection.", uri => uri.Scheme is "amqp" or "amqps" && InfrastructureServiceType.HasNoPath(uri));
    public string Type => "amqp.connection";
    public string? Endpoint(JsonObject resource) => ResourceJson.Text(resource, "Endpoint");
    public void ApplyExisting(JsonObject resource, Uri endpoint) =>
        ResourceJson.Set(resource, "Endpoint", JsonValue.Create(endpoint.AbsoluteUri));

    public void Apply(JsonObject resource, Uri endpoint)
    {
        ApplyExisting(resource, endpoint);
        foreach (var key in new[] { "UsernameEnvironmentVariable", "PasswordEnvironmentVariable" }) ResourceJson.Remove(resource, key);
        ResourceJson.Set(resource, "VirtualHost", JsonValue.Create("/"));
    }

    public void Apply(JsonObject resource, Uri endpoint, ResourceBinding binding)
    {
        Apply(resource, endpoint);
        ResourceJson.Set(resource, "VirtualHost", JsonValue.Create(binding.Variables.GetValueOrDefault("RABBITMQ_DEFAULT_VHOST", "/")));
        if (binding.Variables.GetValueOrDefault("RABBITMQ_DEFAULT_USER_ENVIRONMENT_VARIABLE") is { } user)
            ResourceJson.Set(resource, "UsernameEnvironmentVariable", JsonValue.Create(user));
        if (binding.Variables.GetValueOrDefault("RABBITMQ_DEFAULT_PASS_ENVIRONMENT_VARIABLE") is { } password)
            ResourceJson.Set(resource, "PasswordEnvironmentVariable", JsonValue.Create(password));
    }
}
