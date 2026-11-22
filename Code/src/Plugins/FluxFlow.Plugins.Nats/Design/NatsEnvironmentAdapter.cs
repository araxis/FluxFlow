using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

public sealed class NatsEnvironmentAdapter : IResourceEnvironmentAdapter
{
    public InfrastructureServiceType Infrastructure { get; } = new("nats.connection", "NATS service", "nats://broker.example:4222", "NATS or TLS endpoint without credentials. Authentication and TLS settings belong to the project connection.", uri => uri.Scheme is "nats" or "tls" && InfrastructureServiceType.HasNoPath(uri));
    public string Type => "nats.connection";
    public string? Endpoint(JsonObject resource) => ResourceJson.Text(resource, "Endpoint");
    public void ApplyExisting(JsonObject resource, Uri endpoint) => ResourceJson.Set(resource, "Endpoint", JsonValue.Create(endpoint.AbsoluteUri));
    public void Apply(JsonObject resource, Uri endpoint)
    {
        ApplyExisting(resource, endpoint);
        foreach (var key in new[] { "UsernameEnvironmentVariable", "PasswordEnvironmentVariable", "TokenEnvironmentVariable", "CredentialsFileEnvironmentVariable",
                     "CaFileEnvironmentVariable", "ClientCertificateEnvironmentVariable", "ClientKeyEnvironmentVariable" }) ResourceJson.Remove(resource, key);
        ResourceJson.Set(resource, "TlsMode", JsonValue.Create("Disable"));
    }
    public void Apply(JsonObject resource, Uri endpoint, ResourceBinding binding)
    {
        Apply(resource, endpoint);
        if (binding.Variables.GetValueOrDefault("NATS_TOKEN_ENVIRONMENT_VARIABLE") is { } reference)
            ResourceJson.Set(resource, "TokenEnvironmentVariable", JsonValue.Create(reference));
        if (binding.Variables.GetValueOrDefault("NATS_USERNAME_ENVIRONMENT_VARIABLE") is { } user)
            ResourceJson.Set(resource, "UsernameEnvironmentVariable", JsonValue.Create(user));
        if (binding.Variables.GetValueOrDefault("NATS_PASSWORD_ENVIRONMENT_VARIABLE") is { } password)
            ResourceJson.Set(resource, "PasswordEnvironmentVariable", JsonValue.Create(password));
    }
}
