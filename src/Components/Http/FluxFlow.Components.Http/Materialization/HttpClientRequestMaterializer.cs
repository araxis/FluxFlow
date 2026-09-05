using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Http.Materialization;

/// <summary>Owns the structural contract accepted by the HTTP client component.</summary>
public sealed class HttpClientRequestMaterializer : IFlowValueMaterializer<HttpClientRequest>
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public FlowValueShape InputShape { get; } = FlowValueShape.Object(
        "An HTTP request object with method, url, headers, body, and optional timeout fields.");

    public FlowValueMaterializationResult<HttpClientRequest> Materialize(FlowValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            var request = JsonNode.Parse(value.ToString()) as JsonObject
                ?? throw new JsonException("HTTP request input must be an object.");
            NormalizeBody(request);
            var materialized = request.Deserialize<HttpClientRequest>(SerializerOptions)
                ?? throw new JsonException("HTTP request input cannot be null.");
            return FlowValueMaterializationResult<HttpClientRequest>.Success(materialized);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            return FlowValueMaterializationResult<HttpClientRequest>.Failure(
                new FlowError(
                    "http.request.invalid_shape",
                    "The workflow value cannot be materialized as an HTTP request.",
                    "HTTP",
                    details: JsonSerializer.SerializeToElement(new
                    {
                        target = nameof(HttpClientRequest),
                        reason = exception.Message
                    })));
        }
    }

    private static void NormalizeBody(JsonObject request)
    {
        var bodyProperty = FindProperty(request, "body");
        if (bodyProperty is null || request[bodyProperty] is not { } body)
            return;

        request[bodyProperty] = JsonSerializer.SerializeToNode(
            ToContent(body),
            SerializerOptions);
    }

    private static FlowContent ToContent(JsonNode body)
    {
        if (body is JsonValue scalar && scalar.TryGetValue<string>(out var text))
        {
            return FlowContent.FromBytes(
                Encoding.UTF8.GetBytes(text),
                "text/plain",
                "utf-8");
        }

        if (body is JsonObject content && FindProperty(content, "bytes") is not null)
        {
            return content.Deserialize<FlowContent>(SerializerOptions)
                ?? throw new JsonException("HTTP request body content cannot be null.");
        }

        return FlowContent.FromBytes(
            Encoding.UTF8.GetBytes(body.ToJsonString()),
            "application/json",
            "utf-8");
    }

    private static string? FindProperty(JsonObject value, string name)
    {
        foreach (var property in value)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
                return property.Key;
        }

        return null;
    }
}
