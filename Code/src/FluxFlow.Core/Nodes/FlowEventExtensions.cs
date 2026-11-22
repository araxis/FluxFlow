using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Core.Nodes;

public static class FlowEventDataflowExtensions
{
    public static bool Post(this ITargetBlock<FlowMessage> target, FlowEvent @event)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(@event);
        return DataflowBlock.Post(target, @event.ToMessage());
    }
}

public static class FlowEventMessageExtensions
{
    public static bool TryGetFlowEvent(this FlowMessage message, [NotNullWhen(true)] out FlowEvent? @event)
    {
        ArgumentNullException.ThrowIfNull(message);
        return TryReadEvent(message.Value, message.Timestamp, out @event);
    }

    private static bool TryReadEvent(FlowValue value, DateTimeOffset timestamp,
        [NotNullWhen(true)] out FlowEvent? @event)
    {
        @event = null;
        if (value is FlowEvent typed)
        {
            @event = typed;
            return true;
        }
        if (value is FlowError)
            return false;

        var root = value.Json;
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetString(root, nameof(FlowEvent.Name), out var name) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        @event = new FlowEvent
        {
            Name = name,
            Kind = ReadEnum(root, nameof(FlowEvent.Kind), FlowEventKind.Diagnostic),
            Level = ReadEnum(root, nameof(FlowEvent.Level), FlowEventLevel.Information),
            Message = TryGetString(root, nameof(FlowEvent.Message), out var text) ? text : null,
            Dimensions = ReadObject(root, nameof(FlowEvent.Dimensions)),
            Measurements = ReadMeasurements(root),
            Details = ReadFlowValue(root, nameof(FlowEvent.Details)),
            Attributes = ReadObject(root, nameof(FlowEvent.Attributes)),
            Timestamp = ReadTimestamp(root, timestamp)
        };
        return true;
    }

    public static FlowEvent ToFlowEvent(this FlowMessage message)
        => message.TryGetFlowEvent(out var @event)
            ? @event
            : throw new InvalidOperationException("The flow message does not contain a canonical event value.");

    public static bool TryGetFlowEvent(this FlowMessage<FlowValue> message, [NotNullWhen(true)] out FlowEvent? @event)
        => ((FlowMessage)message).TryGetFlowEvent(out @event);

    public static FlowEvent ToFlowEvent(this FlowMessage<FlowValue> message)
        => ((FlowMessage)message).ToFlowEvent();

    public static bool TryGetFlowEvent(this FlowValue value, [NotNullWhen(true)] out FlowEvent? @event)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryReadEvent(value, DateTimeOffset.UtcNow, out @event);
    }

    public static FlowEvent ToFlowEvent(this FlowValue value)
        => value.TryGetFlowEvent(out var @event)
            ? @event
            : throw new InvalidOperationException("The flow value does not contain a canonical event value.");

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement root, string name, TEnum fallback)
        where TEnum : struct, Enum
        => TryGetString(root, name, out var value) &&
           Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;

    private static IReadOnlyDictionary<string, object?> ReadObject(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?>(StringComparer.Ordinal);

        return property.EnumerateObject().ToDictionary(
            static item => item.Name,
            static item => ReadValue(item.Value),
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, double> ReadMeasurements(JsonElement root)
    {
        if (!root.TryGetProperty(nameof(FlowEvent.Measurements), out var property) || property.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, double>(StringComparer.Ordinal);

        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateObject())
        {
            if (!string.IsNullOrWhiteSpace(item.Name) && item.Value.ValueKind == JsonValueKind.Number &&
                item.Value.TryGetDouble(out var value) && double.IsFinite(value))
                result[item.Name] = value;
        }

        return result;
    }

    private static FlowValue ReadFlowValue(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
            ? new JsonFlowValue(value)
            : JsonFlowValue.Null;

    private static DateTimeOffset ReadTimestamp(JsonElement root, DateTimeOffset fallback)
        => root.TryGetProperty(nameof(FlowEvent.Timestamp), out var value) &&
           value.ValueKind == JsonValueKind.String &&
           value.TryGetDateTimeOffset(out var timestamp)
            ? timestamp
            : fallback;

    private static object? ReadValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.TryGetDateTimeOffset(out var date)
                ? date
                : value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var integer)
                ? (object)integer
                : value.GetDouble(),
            JsonValueKind.Array or JsonValueKind.Object => value.Clone(),
            _ => value.GetRawText()
        };
}
