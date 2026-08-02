using System.Text.Json;

namespace FluxFlow.Composition;

internal static class CompositionProcessingConfiguration
{
    internal const string BoundedCapacity = "BoundedCapacity";
    internal const string MaxDegreeOfParallelism = "MaxDegreeOfParallelism";
    internal const string EnsureOrdered = "EnsureOrdered";

    internal static IReadOnlyList<string> TechnicalOptionNames { get; } =
    [
        BoundedCapacity,
        MaxDegreeOfParallelism,
        EnsureOrdered
    ];

    internal static void Apply(
        IDictionary<string, JsonElement> properties,
        CompositionProcessingSettings settings,
        JsonSerializerOptions serializerOptions)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(serializerOptions);

        AddIfMissing(properties, BoundedCapacity, settings.BufferCapacity, serializerOptions);
        AddIfMissing(properties, MaxDegreeOfParallelism, settings.Concurrency, serializerOptions);
        AddIfMissing(properties, EnsureOrdered, settings.PreserveOrder, serializerOptions);
    }

    private static void AddIfMissing<T>(
        IDictionary<string, JsonElement> properties,
        string name,
        T value,
        JsonSerializerOptions serializerOptions)
    {
        if (properties.Keys.Any(key =>
                string.Equals(key, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        properties.Add(name, JsonSerializer.SerializeToElement(value, serializerOptions));
    }
}
