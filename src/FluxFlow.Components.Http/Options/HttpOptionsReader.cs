using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Http.Options;

internal static class HttpOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static HttpClientOptions ReadClientOptions(NodeDefinition definition)
    {
        var options = Read<HttpClientOptions>(definition);

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "http.client option 'baseUrl' must be an absolute URL.");
        }

        if (options.RestrictToBaseUrlOrigin && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException(
                "http.client option 'restrictToBaseUrlOrigin' requires 'baseUrl'.");
        }

        if (options.AllowedHosts.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "http.client option 'allowedHosts' cannot contain empty entries.");
        }

        if (options.DefaultTimeoutMilliseconds <= 0)
        {
            throw new InvalidOperationException(
                "http.client option 'defaultTimeoutMilliseconds' must be greater than zero.");
        }

        if (options.PooledConnectionLifetimeSeconds is <= 0)
        {
            throw new InvalidOperationException(
                "http.client option 'pooledConnectionLifetimeSeconds' must be greater than zero when set.");
        }

        if (options.MaxConnectionsPerServer is <= 0)
        {
            throw new InvalidOperationException(
                "http.client option 'maxConnectionsPerServer' must be greater than zero when set.");
        }

        return options;
    }

    public static HttpRequestNodeOptions ReadRequestOptions(NodeDefinition definition)
    {
        var options = Read<HttpRequestNodeOptions>(definition);

        if (string.IsNullOrWhiteSpace(options.Client))
        {
            throw new InvalidOperationException(
                "http.request option 'client' is required and must name an http.client resource.");
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
