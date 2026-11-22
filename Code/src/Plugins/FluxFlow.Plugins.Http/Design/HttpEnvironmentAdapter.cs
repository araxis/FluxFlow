using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

public sealed class HttpEnvironmentAdapter : IResourceEnvironmentAdapter
{
    public void ApplyExisting(JsonObject resource, Uri endpoint) => ResourceJson.Set(resource, "BaseAddress", JsonValue.Create(endpoint.AbsoluteUri));
    public InfrastructureServiceType Infrastructure { get; } = new("http.client", "HTTP service", "https://api.example/", "HTTP or HTTPS endpoint without credentials.", uri => uri.Scheme is "http" or "https");
    public string Type => "http.client";
    public string? Endpoint(JsonObject resource) => ResourceJson.Text(resource, "BaseAddress");
    public void Apply(JsonObject resource, Uri endpoint)
    {
        var uri = Uri.TryCreate(Endpoint(resource), UriKind.Absolute, out var original) ? new UriBuilder(original) : new UriBuilder(endpoint);
        uri.Scheme = endpoint.Scheme; uri.Host = endpoint.Host; uri.Port = endpoint.Port;
        uri.UserName = ""; uri.Password = "";
        ResourceJson.Set(resource, "BaseAddress", JsonValue.Create(uri.Uri.AbsoluteUri));
        // A mock must not receive credentials intended for the original service.
        ResourceJson.Remove(resource, "Authentication");
        if (ResourceJson.Value(resource, "Headers") is JsonObject headers) ResourceJson.Remove(headers, "Authorization");
    }
}
