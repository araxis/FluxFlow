using System.Text.Json.Nodes;
using FluxFlow.Configuration.Components.RateLimiting;
using FluxFlow.Environments;

namespace FluxFlow.Plugins.FlowControl;

public sealed class TokenBucketEnvironmentAdapter : IResourceEnvironmentAdapter
{
    public string Type => TokenBucketConfiguration.ResourceType;
    public string? Endpoint(JsonObject resource) => null;
    public void Apply(JsonObject resource, Uri endpoint) =>
        throw new ArgumentException("Rate limiters run inside the application and cannot use a service endpoint.", nameof(endpoint));
}
