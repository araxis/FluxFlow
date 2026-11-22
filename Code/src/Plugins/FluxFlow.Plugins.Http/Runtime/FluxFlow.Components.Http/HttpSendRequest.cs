using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Http;

[JsonConverter(typeof(JsonStringEnumConverter<HttpBodyEncoding>))]
public enum HttpBodyEncoding { Text, Base64, Json }

public sealed class HttpSendRequest : FlowValue
{
    private IReadOnlyDictionary<string, string> _headers = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    public string Method { get; init; } = "GET";
    public required string RequestUrl { get; init; }
    public IReadOnlyDictionary<string, string> Headers
    {
        get => _headers;
        init => _headers = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase));
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FlowValue? Body { get; init; }
    /// <summary>Multipart fields and local files. Mutually exclusive with Body.</summary>
    public HttpForm? Form { get; init; }
    public string? ContentType { get; init; }
    public HttpBodyEncoding Encoding { get; init; }
    public HttpBodyEncoding ResponseEncoding { get; init; }
    /// <summary>Optional total request deadline. The native client's timeout remains an upper bound.</summary>
    public TimeSpan? Timeout { get; init; }

    protected override JsonElement CreateJson()
        => JsonSerializer.SerializeToElement(this, HttpJsonContext.Default.HttpSendRequest);
}
