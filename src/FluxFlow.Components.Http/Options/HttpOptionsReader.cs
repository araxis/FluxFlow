using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Http.Options;

internal static class HttpOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static HttpClientNodeOptions ReadNodeOptions(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        var options = JsonSerializer.Deserialize<HttpClientNodeOptions>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Could not read http.client options.");

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "http.client option 'boundedCapacity' must be greater than zero.");
        }

        if (options.MaxResponseBodyBytes <= 0)
        {
            throw new InvalidOperationException(
                "http.client option 'maxResponseBodyBytes' must be greater than zero.");
        }

        if (options.MaxDegreeOfParallelism <= 0)
        {
            throw new InvalidOperationException(
                "http.client option 'maxDegreeOfParallelism' must be greater than zero.");
        }

        if (options.DefaultTimeoutMilliseconds is <= 0)
        {
            throw new InvalidOperationException(
                "http.client option 'defaultTimeoutMilliseconds' must be greater than zero when set.");
        }

        return options;
    }
}
