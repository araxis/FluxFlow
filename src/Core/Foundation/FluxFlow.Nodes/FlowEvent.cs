using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;

namespace FluxFlow.Nodes;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlowEventKind
{
    Domain,
    Lifecycle,
    Diagnostic,
    Audit
}

public static class FlowEventHeaders
{
    public const string Type = "flow.event.type";
    public const string Kind = "flow.event.kind";
    public const string Level = "flow.event.level";
    public const string Source = "flow.event.source";
    public const string Application = "flow.event.application";
    public const string Revision = "flow.event.revision";
    public const string Workflow = "flow.event.workflow";
    public const string Component = "flow.event.component";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlowEventLevel
{
    Trace,
    Information,
    Warning,
    Error
}

/// <summary>
/// Authoring and access model for an event payload. Runtime <c>Events</c> ports
/// carry canonical <see cref="FlowMessage"/> envelopes, not this helper type.
/// </summary>
public sealed record FlowEvent
{
    private IReadOnlyDictionary<string, object?> _attributes =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The correlation id of the message this event relates to, if any.</summary>
    public CorrelationId? CorrelationId { get; init; }

    [JsonPropertyName("type")]
    public required string Name { get; init; }

    public FlowEventKind Kind { get; init; } = FlowEventKind.Diagnostic;

    public FlowEventLevel Level { get; init; } = FlowEventLevel.Information;

    public string? Message { get; init; }

    public IReadOnlyDictionary<string, object?> Attributes
    {
        get => _attributes;
        init => _attributes = CopyAttributes(value);
    }

    public IReadOnlyDictionary<string, object?> Dimensions { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, double> Measurements { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);

    public FlowValue Details { get; init; } = FlowValue.Null;

    public FlowMessage ToMessage()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        var timestamp = Timestamp == default ? DateTimeOffset.UtcNow : Timestamp;
        var kind = Kind.ToString().ToLowerInvariant();
        var level = Level.ToString().ToLowerInvariant();
        var value = FlowValue.From(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = Name.Trim(),
            ["kind"] = kind,
            ["level"] = level,
            ["timestamp"] = timestamp,
            ["message"] = Message,
            ["dimensions"] = ConvertValues(Dimensions),
            ["measurements"] = CopyMeasurements(Measurements),
            ["details"] = Details,
            ["attributes"] = ConvertValues(Attributes)
        });
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FlowEventHeaders.Type] = Name.Trim(),
            [FlowEventHeaders.Kind] = kind,
            [FlowEventHeaders.Level] = level
        };
        var created = FlowMessage.Create(value, CorrelationId, headers: headers);
        return FlowMessage.Restore(
            value,
            created.MessageId,
            created.TraceId,
            timestamp,
            created.CorrelationId,
            created.CausationId,
            created.Headers);
    }

    private static IReadOnlyDictionary<string, object?> CopyAttributes(
        IReadOnlyDictionary<string, object?>? attributes)
        => attributes is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(attributes, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, FlowValue> ConvertValues(
        IReadOnlyDictionary<string, object?> values)
    {
        var converted = new Dictionary<string, FlowValue>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            try
            {
                converted[name] = FlowValue.From(value);
            }
            catch
            {
                converted[name] = FlowValue.From(
                    value is IFormattable formattable
                        ? formattable.ToString(null, CultureInfo.InvariantCulture)
                        : value?.ToString());
            }
        }

        return converted;
    }

    private static IReadOnlyDictionary<string, double> CopyMeasurements(
        IReadOnlyDictionary<string, double> measurements)
    {
        var copy = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (name, value) in measurements)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(measurements),
                    $"Event measurement '{name}' must be finite.");
            }

            copy.Add(name, value);
        }

        return copy;
    }
}

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
    public static bool TryGetFlowEvent(this FlowMessage message, out FlowEvent? @event)
    {
        ArgumentNullException.ThrowIfNull(message);
        @event = null;
        if (message.IsError)
            return false;

        var root = message.Value.ToJsonElement();
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetString(root, "type", out var name))
        {
            return false;
        }

        @event = new FlowEvent
        {
            Name = name,
            Kind = ReadEnum(root, "kind", FlowEventKind.Diagnostic),
            Level = ReadEnum(root, "level", FlowEventLevel.Information),
            Message = TryGetString(root, "message", out var text) ? text : null,
            Dimensions = ReadObject(root, "dimensions"),
            Measurements = ReadMeasurements(root),
            Details = ReadFlowValue(root, "details"),
            Attributes = ReadObject(root, "attributes"),
            Timestamp = ReadTimestamp(root, message.Timestamp),
            CorrelationId = message.CorrelationId
        };
        return true;
    }

    public static FlowEvent ToFlowEvent(this FlowMessage message)
        => message.TryGetFlowEvent(out var @event)
            ? @event
            : throw new InvalidOperationException("The flow message does not contain a canonical event value.");

    public static bool TryGetFlowEvent(
        this FlowMessage<FlowValue> message,
        out FlowEvent? @event)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.Value.TryGetFlowEvent(out @event))
            return false;

        @event = @event with
        {
            Timestamp = message.Timestamp,
            CorrelationId = message.CorrelationId
        };
        return true;
    }

    public static FlowEvent ToFlowEvent(this FlowMessage<FlowValue> message)
        => message.TryGetFlowEvent(out var @event)
            ? @event
            : throw new InvalidOperationException("The flow message does not contain a canonical event value.");

    public static bool TryGetFlowEvent(this FlowValue value, out FlowEvent? @event)
    {
        ArgumentNullException.ThrowIfNull(value);
        return FlowMessage.Create(value).TryGetFlowEvent(out @event);
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
        if (!root.TryGetProperty("measurements", out var property) || property.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, double>(StringComparer.Ordinal);

        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateObject())
        {
            if (item.Value.TryGetDouble(out var value))
                result[item.Name] = value;
        }

        return result;
    }

    private static FlowValue ReadFlowValue(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
            ? FlowValue.FromJson(value)
            : FlowValue.Null;

    private static DateTimeOffset ReadTimestamp(JsonElement root, DateTimeOffset fallback)
        => root.TryGetProperty("timestamp", out var value) &&
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
                ? integer
                : value.GetDouble(),
            JsonValueKind.Array => value.EnumerateArray().Select(ReadValue).ToArray(),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(
                static item => item.Name,
                static item => ReadValue(item.Value),
                StringComparer.Ordinal),
            _ => value.GetRawText()
        };
}
