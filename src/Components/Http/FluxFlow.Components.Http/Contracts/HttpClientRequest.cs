using System.Text.Json;
using System.Collections.Immutable;
using FluxFlow.Data;

namespace FluxFlow.Components.Http.Contracts;

public sealed class HttpClientRequest : FlowValue
{
    protected override System.Text.Json.JsonElement CreateJson()
        => JsonSerializer.SerializeToElement(this, HttpRequestJsonContext.Default.HttpClientRequest);

    private IReadOnlyDictionary<string, string> _headers =
        ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);

    public string Method { get; init; } = "GET";

    public string? Url { get; init; }

    public IReadOnlyDictionary<string, string> Headers
    {
        get => _headers;
        init => _headers = value is null || value.Count == 0
            ? ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase)
            : value.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public FlowContent? Body { get; init; }

    public TimeSpan? Timeout { get; init; }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(FlowContent))]
[System.Text.Json.Serialization.JsonSerializable(typeof(HttpClientRequest))]
internal sealed partial class HttpRequestJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
