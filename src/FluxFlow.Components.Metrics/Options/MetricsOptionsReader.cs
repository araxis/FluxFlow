using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Metrics.Options;

internal static class MetricsOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static MetricsAggregateOptions ReadAggregateOptions(NodeDefinition definition)
    {
        var options = Read<MetricsAggregateOptions>(definition);

        if (options.RateWindowSeconds <= 0)
        {
            throw new InvalidOperationException(
                "metrics.aggregate option 'rateWindowSeconds' must be greater than zero.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "metrics.aggregate option 'boundedCapacity' must be greater than zero.");
        }

        if (options.MaxGroups < 0)
        {
            throw new InvalidOperationException(
                "metrics.aggregate option 'maxGroups' must be zero or greater.");
        }

        if (options.GroupByTag is not null &&
            string.IsNullOrWhiteSpace(options.GroupByTag))
        {
            throw new InvalidOperationException(
                "metrics.aggregate option 'groupByTag' cannot be empty when set.");
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
