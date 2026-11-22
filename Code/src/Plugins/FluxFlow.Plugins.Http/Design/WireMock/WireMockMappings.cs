using System.Text.Json.Nodes;

using FluxFlow.Environments;

namespace FluxFlow.Plugins.Http.WireMock;

public static class WireMockMappings
{
    public static EnvironmentProviderDescription Provider { get; } = new(
        "wiremock", "http.client", "WireMock.NET", "sheyenrath/wiremock.net-alpine:2.14.0")
    {
        Document = new("HTTP endpoints", "endpoint", "Endpoints", "Define requests and their mock responses.",
            binding => binding.Mappings, (binding, json) => binding.Profile?.SetupLocked == true || binding.Mappings == json
                ? binding : binding with { Mappings = json }, Validate,
            () => (JsonObject)JsonNode.Parse("""{"Request":{"Methods":["GET"],"Path":"/"},"Response":{"StatusCode":200,"Headers":{"Content-Type":"application/json"},"BodyAsJson":{}}}""")!,
            mapping => new WireMockEndpointDraft(mapping))
    };

    public static void Validate(string value)
    {
        var node = JsonNode.Parse(value);
        var mappings = node is JsonArray array ? array.ToArray() : [node];
        if (mappings.Any(item => item is not JsonObject || item["Request"] is not JsonObject || item["Response"] is not JsonObject))
            throw new ArgumentException("Each WireMock.NET mapping requires Request and Response objects.");
    }
}
