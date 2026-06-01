using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Http.Options;

internal static class HttpOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static HttpRequestNodeOptions ReadRequestOptions(NodeDefinition definition)
    {
        var options = Read<HttpRequestNodeOptions>(definition);

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "http.request option 'baseUrl' must be an absolute URL.");
        }

        if (options.DefaultTimeoutMilliseconds <= 0)
        {
            throw new InvalidOperationException(
                "http.request option 'defaultTimeoutMilliseconds' must be greater than zero.");
        }

        if (options.MaxResponseBodyBytes <= 0)
        {
            throw new InvalidOperationException(
                "http.request option 'maxResponseBodyBytes' must be greater than zero.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "http.request option 'boundedCapacity' must be greater than zero.");
        }

        return options;
    }

    private static T Read<T>(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not read {typeof(T).Name}.");
    }
}
