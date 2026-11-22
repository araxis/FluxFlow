using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Core.Nodes;

[JsonConverter(typeof(JsonStringEnumConverter<FlowEventKind>))]
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
    public const string Workflow = "flow.event.workflow";
    public const string Component = "flow.event.component";
}

[JsonConverter(typeof(JsonStringEnumConverter<FlowEventLevel>))]
public enum FlowEventLevel
{
    Trace,
    Information,
    Warning,
    Error
}

/// <summary>
/// Immutable event data with a lazy JSON representation. Runtime <c>Events</c> ports carry
/// <see cref="FlowMessage"/> envelopes created by <see cref="ToMessage"/>.
/// </summary>
public sealed class FlowEvent : FlowValue
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private IReadOnlyDictionary<string, object?> _attributes = ImmutableDictionary<string, object?>.Empty;
    private IReadOnlyDictionary<string, object?> _dimensions = ImmutableDictionary<string, object?>.Empty;
    private IReadOnlyDictionary<string, double> _measurements = ImmutableDictionary<string, double>.Empty;
    private readonly DateTimeOffset _timestamp = DateTimeOffset.UtcNow;
    private readonly string _name = string.Empty;

    public DateTimeOffset Timestamp
    {
        get => _timestamp;
        init => _timestamp = value == default ? DateTimeOffset.UtcNow : value;
    }

    public required string Name
    {
        get => _name;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _name = value.Trim();
        }
    }

    public FlowEventKind Kind { get; init; } = FlowEventKind.Diagnostic;

    public FlowEventLevel Level { get; init; } = FlowEventLevel.Information;

    public string? Message { get; init; }

    /// <summary>Event properties. Immutable scalars and FlowValues stay typed; other values are owned JSON snapshots.</summary>
    public IReadOnlyDictionary<string, object?> Attributes
    {
        get => _attributes;
        init => _attributes = SnapshotValues(value);
    }

    /// <summary>Grouping labels, using the same snapshot rules as Attributes.</summary>
    public IReadOnlyDictionary<string, object?> Dimensions
    {
        get => _dimensions;
        init => _dimensions = SnapshotValues(value);
    }

    /// <summary>Named finite measurements. Exporters decide how to interpret them.</summary>
    public IReadOnlyDictionary<string, double> Measurements
    {
        get => _measurements;
        init => _measurements = CopyMeasurements(value);
    }

    public FlowValue Details { get; init; } = JsonFlowValue.Null;

    /// <summary>Wraps this value without serialization. Correlation belongs to the envelope.</summary>
    public FlowMessage ToMessage(CorrelationId? correlationId = null)
    {
        var kind = Kind.ToString().ToLowerInvariant();
        var level = Level.ToString().ToLowerInvariant();
        var headers = new Dictionary<string, FlowValue>(3, StringComparer.Ordinal)
        {
            [FlowEventHeaders.Type] = Name,
            [FlowEventHeaders.Kind] = kind,
            [FlowEventHeaders.Level] = level
        };
        return new FlowMessage(this, correlationId, traceId: null, headers: headers,
            causationId: null, timestamp: Timestamp);
    }

    protected override JsonElement CreateJson()
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>(9, StringComparer.Ordinal)
        {
            [nameof(Name)] = Name,
            [nameof(Kind)] = Kind.ToString().ToLowerInvariant(),
            [nameof(Level)] = Level.ToString().ToLowerInvariant(),
            [nameof(Timestamp)] = Timestamp,
            [nameof(Message)] = Message,
            [nameof(Dimensions)] = JsonValues(Dimensions),
            [nameof(Measurements)] = Measurements,
            [nameof(Details)] = Details.Json,
            [nameof(Attributes)] = JsonValues(Attributes)
        }, typeof(Dictionary<string, object?>), FlowJsonContext.Default);

    private static Dictionary<string, object?> JsonValues(IReadOnlyDictionary<string, object?> values)
        => values.ToDictionary(pair => pair.Key,
            pair => JsonValue(pair.Value), StringComparer.Ordinal);

    private static object? JsonValue(object? value)
    {
        try { return value is FlowValue flowValue ? flowValue.Json : value; }
        catch { return FormatUnsupported(value); }
    }

    private static string? FormatUnsupported(object? value)
        => value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString();

    private static IReadOnlyDictionary<string, object?> SnapshotValues(
        IReadOnlyDictionary<string, object?>? values)
    {
        if (values is null || values.Count == 0) return ImmutableDictionary<string, object?>.Empty;
        var converted = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            try
            {
                // Keep immutable values typed; own mutable objects and borrowed JSON now.
                converted[name] = value switch
                {
                    FlowValue => value,
                    null or string or bool or char or byte or sbyte or short or ushort or int or uint
                        or long or ulong or decimal or DateTime or DateTimeOffset
                        or TimeSpan or Guid => value,
                    double number when double.IsFinite(number) => value,
                    float number when float.IsFinite(number) => value,
                    JsonElement json when json.ValueKind != JsonValueKind.Undefined => json.Clone(),
                    _ => JsonSerializer.SerializeToElement(value, JsonOptions)
                };
            }
            catch
            {
                converted[name] = FormatUnsupported(value);
            }
        }

        return converted.ToImmutable();
    }

    private static IReadOnlyDictionary<string, double> CopyMeasurements(
        IReadOnlyDictionary<string, double> measurements)
    {
        if (measurements.Count == 0) return ImmutableDictionary<string, double>.Empty;
        var copy = ImmutableDictionary.CreateBuilder<string, double>(StringComparer.Ordinal);
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

        return copy.ToImmutable();
    }
}
