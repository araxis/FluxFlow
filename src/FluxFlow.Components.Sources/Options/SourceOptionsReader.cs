using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Sources.Options;

internal static class SourceOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static GeneratedSourceOptions ReadGeneratedOptions(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.Configuration.ContainsKey("items"))
        {
            throw new InvalidOperationException("source.generated requires configuration value 'items'.");
        }

        var options = Read<GeneratedSourceOptions>(definition);
        ValidateCommon(
            "source.generated",
            options.BoundedCapacity,
            options.InitialDelayMilliseconds,
            options.IntervalMilliseconds);
        if (string.IsNullOrWhiteSpace(options.OutputType))
        {
            throw new InvalidOperationException("source.generated option 'outputType' cannot be empty.");
        }

        if (options.MaxItems.HasValue && options.MaxItems.Value <= 0)
        {
            throw new InvalidOperationException("source.generated option 'maxItems' must be greater than zero.");
        }

        if (options.Loop && !options.MaxItems.HasValue)
        {
            throw new InvalidOperationException("source.generated option 'maxItems' is required when 'loop' is true.");
        }

        return options;
    }

    public static SequenceSourceOptions ReadSequenceOptions(NodeDefinition definition)
    {
        var options = Read<SequenceSourceOptions>(definition);
        ValidateCommon(
            "source.sequence",
            options.BoundedCapacity,
            options.InitialDelayMilliseconds,
            options.IntervalMilliseconds);
        if (options.Count <= 0)
        {
            throw new InvalidOperationException("source.sequence option 'count' must be greater than zero.");
        }

        if (options.Step == 0)
        {
            throw new InvalidOperationException("source.sequence option 'step' cannot be zero.");
        }

        return options;
    }

    private static void ValidateCommon(
        string nodeType,
        int boundedCapacity,
        int initialDelayMilliseconds,
        int intervalMilliseconds)
    {
        if (boundedCapacity <= 0)
        {
            throw new InvalidOperationException($"{nodeType} option 'boundedCapacity' must be greater than zero.");
        }

        if (initialDelayMilliseconds < 0)
        {
            throw new InvalidOperationException($"{nodeType} option 'initialDelayMilliseconds' cannot be negative.");
        }

        if (intervalMilliseconds < 0)
        {
            throw new InvalidOperationException($"{nodeType} option 'intervalMilliseconds' cannot be negative.");
        }
    }

    private static T Read<T>(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not read {typeof(T).Name}.");
    }
}
