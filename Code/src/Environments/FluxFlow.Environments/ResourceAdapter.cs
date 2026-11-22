using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

public interface IResourceEnvironmentAdapter
{
    InfrastructureServiceType? Infrastructure => null;
    string? ExclusiveKey(Uri endpoint) => null;
    string Type { get; }
    string? Endpoint(JsonObject resource);
    void Apply(JsonObject resource, Uri endpoint);
    void ApplyExisting(JsonObject resource, Uri endpoint) => Apply(resource, endpoint);
    void ApplyExisting(JsonObject resource, InfrastructureService service) => ApplyExisting(resource, new Uri(service.Endpoint));
    void Apply(JsonObject resource, Uri endpoint, ResourceBinding binding) => Apply(resource, endpoint);
}

public sealed class EndpointEnvironmentAdapter(InfrastructureServiceType infrastructure) : IResourceEnvironmentAdapter
{
    public InfrastructureServiceType Infrastructure => infrastructure;
    public string Type => Infrastructure.Type;
    public string? Endpoint(JsonObject resource) => ResourceJson.Text(resource, "Endpoint");
    public void Apply(JsonObject resource, Uri endpoint)
    {
        new InfrastructureService { Type = Type, Endpoint = endpoint.AbsoluteUri }.Validate(Infrastructure);
        ResourceJson.Set(resource, "Endpoint", JsonValue.Create(endpoint.AbsoluteUri));
    }
}

public static class ResourceJson
{
    public static string Key(JsonObject value, string key) => value.Select(pair => pair.Key).FirstOrDefault(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;
    public static JsonNode? Value(JsonObject value, string key) => value[Key(value, key)];
    public static string? Text(JsonObject value, string key) => Value(value, key)?.GetValue<string>();
    public static void Set(JsonObject value, string key, JsonNode? content) => value[Key(value, key)] = content;
    public static void Remove(JsonObject value, string key) => value.Remove(Key(value, key));
}
