using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Observability.Options;

internal static class ObservabilityOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static FlowCounterOptions ReadCounterOptions(NodeDefinition definition)
    {
        var options = Read<FlowCounterOptions>(definition);

        ValidateBoundedCapacity("flow.counter", options.BoundedCapacity);
        ValidateType("flow.counter", options.InputType);
        if (string.IsNullOrWhiteSpace(options.Predicate) &&
            string.IsNullOrWhiteSpace(options.Expression))
        {
            return options;
        }

        if (!string.IsNullOrWhiteSpace(options.Predicate) &&
            !string.IsNullOrWhiteSpace(options.Expression))
        {
            throw new InvalidOperationException(
                "flow.counter cannot set both 'predicate' and 'expression'.");
        }

        return options;
    }

    public static FlowLoggerOptions ReadLoggerOptions(NodeDefinition definition)
    {
        var options = Read<FlowLoggerOptions>(definition);

        ValidateBoundedCapacity("flow.logger", options.BoundedCapacity);
        ValidateType("flow.logger", options.InputType);
        ResolveLogLevel(options.Level);
        foreach (var selector in options.AttributeSelectors ?? [])
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                throw new InvalidOperationException(
                    "flow.logger option 'attributeSelectors' cannot contain empty values.");
            }
        }

        return options;
    }

    public static FlowMetricsOptions ReadMetricsOptions(NodeDefinition definition)
    {
        var options = Read<FlowMetricsOptions>(definition);

        ValidateBoundedCapacity("flow.metrics", options.BoundedCapacity);
        ValidateType("flow.metrics", options.InputType);
        if (options.SizeSelector is not null &&
            string.IsNullOrWhiteSpace(options.SizeSelector))
        {
            throw new InvalidOperationException(
                "flow.metrics option 'sizeSelector' cannot be empty when set.");
        }

        return options;
    }

    public static FlowLogLevel ResolveLogLevel(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Enum.TryParse<FlowLogLevel>(value, ignoreCase: true, out var level))
        {
            throw new InvalidOperationException(
                $"flow.logger option 'level' contains unsupported value '{value}'.");
        }

        return level;
    }

    private static T Read<T>(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not read {typeof(T).Name}.");
    }

    private static void ValidateBoundedCapacity(string nodeType, int boundedCapacity)
    {
        if (boundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'boundedCapacity' must be greater than zero.");
        }
    }

    private static void ValidateType(string nodeType, string inputType)
    {
        if (string.IsNullOrWhiteSpace(inputType))
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'inputType' cannot be empty.");
        }
    }
}
