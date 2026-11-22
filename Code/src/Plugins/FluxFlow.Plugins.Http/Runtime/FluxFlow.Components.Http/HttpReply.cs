using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Http;

/// <summary>A workflow-produced HTTP response. Text bodies are sent as text; other values as JSON.</summary>
public sealed class HttpReply : FlowValue
{
    public int StatusCode { get; }
    public FlowValue? Body { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public string? ContentType { get; }

    [JsonConstructor]
    public HttpReply(int statusCode = 200, FlowValue? body = null,
        IReadOnlyDictionary<string, string>? headers = null, string? contentType = null)
    {
        if (statusCode is < 200 or > 599) throw new ArgumentOutOfRangeException(nameof(statusCode));
        StatusCode = statusCode;
        Body = body;
        Headers = headers?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, string>.Empty;
        ContentType = contentType;
    }

    public static HttpReply Read(FlowValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value as HttpReply ?? value.Json.Deserialize(HttpReplyJsonContext.Default.HttpReply)
            ?? throw new JsonException("Expected an HTTP reply.");
    }

    protected override JsonElement CreateJson() => JsonSerializer.SerializeToElement(this, HttpReplyJsonContext.Default.HttpReply);
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HttpReply))]
internal sealed partial class HttpReplyJsonContext : JsonSerializerContext;
