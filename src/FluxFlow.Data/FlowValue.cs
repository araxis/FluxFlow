using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Serialization;

namespace FluxFlow.Data;

[JsonConverter(typeof(FlowValueJsonConverter))]
public sealed class FlowValue : IEquatable<FlowValue>
{
    private static readonly ImmutableDictionary<string, FlowValue> EmptyObjectValue =
        ImmutableDictionary.Create<string, FlowValue>(StringComparer.Ordinal);

    private readonly object? _value;

    private FlowValue(FlowValueKind kind, object? value)
    {
        Kind = kind;
        _value = value;
    }

    public static FlowValue Null { get; } = new(FlowValueKind.Null, null);

    public FlowValueKind Kind { get; }

    public static FlowValue From(bool value) => new(FlowValueKind.Boolean, value);

    public static FlowValue From(BigInteger value) => new(FlowValueKind.Integer, value);

    public static FlowValue From(long value) => From(new BigInteger(value));

    public static FlowValue From(decimal value) => new(FlowValueKind.Decimal, value);

    public static FlowValue From(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Floating-point values must be finite.");

        return new FlowValue(FlowValueKind.FloatingPoint, value == 0d ? 0d : value);
    }

    public static FlowValue From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new FlowValue(FlowValueKind.String, value);
    }

    public static FlowValue From(DateTimeOffset value) => new(FlowValueKind.DateTimeOffset, value);

    public static FlowValue From(DateOnly value) => new(FlowValueKind.Date, value);

    public static FlowValue From(TimeOnly value) => new(FlowValueKind.Time, value);

    public static FlowValue From(TimeSpan value) => new(FlowValueKind.Duration, value);

    public static FlowValue From(Guid value) => new(FlowValueKind.Guid, value);

    public static FlowValue FromBinary(ReadOnlyMemory<byte> value)
        => new(FlowValueKind.Binary, ImmutableArray.CreateRange(value.ToArray()));

    public static FlowValue FromBinary(ImmutableArray<byte> value)
        => new(FlowValueKind.Binary, value.IsDefault ? ImmutableArray<byte>.Empty : value);

    public static FlowValue FromArray(IEnumerable<FlowValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var builder = ImmutableArray.CreateBuilder<FlowValue>();
        foreach (var value in values)
        {
            builder.Add(value ?? throw new ArgumentException(
                "FlowValue arrays cannot contain null references; use FlowValue.Null.",
                nameof(values)));
        }

        return new FlowValue(FlowValueKind.Array, builder.ToImmutable());
    }

    public static FlowValue FromObject(IEnumerable<KeyValuePair<string, FlowValue>> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var builder = ImmutableDictionary.CreateBuilder<string, FlowValue>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            ArgumentNullException.ThrowIfNull(property.Key);
            if (property.Value is null)
            {
                throw new ArgumentException(
                    "FlowValue objects cannot contain null references; use FlowValue.Null.",
                    nameof(properties));
            }

            if (!builder.TryAdd(property.Key, property.Value))
            {
                throw new ArgumentException(
                    $"FlowValue object contains duplicate property '{property.Key}'.",
                    nameof(properties));
            }
        }

        return new FlowValue(
            FlowValueKind.Object,
            builder.Count == 0 ? EmptyObjectValue : builder.ToImmutable());
    }

    public bool GetBoolean() => GetValue<bool>(FlowValueKind.Boolean);

    public BigInteger GetInteger() => GetValue<BigInteger>(FlowValueKind.Integer);

    public decimal GetDecimal() => GetValue<decimal>(FlowValueKind.Decimal);

    public double GetFloatingPoint() => GetValue<double>(FlowValueKind.FloatingPoint);

    public string GetString() => GetValue<string>(FlowValueKind.String);

    public ImmutableArray<byte> GetBinary() => GetValue<ImmutableArray<byte>>(FlowValueKind.Binary);

    public DateTimeOffset GetDateTimeOffset() => GetValue<DateTimeOffset>(FlowValueKind.DateTimeOffset);

    public DateOnly GetDate() => GetValue<DateOnly>(FlowValueKind.Date);

    public TimeOnly GetTime() => GetValue<TimeOnly>(FlowValueKind.Time);

    public TimeSpan GetDuration() => GetValue<TimeSpan>(FlowValueKind.Duration);

    public Guid GetGuid() => GetValue<Guid>(FlowValueKind.Guid);

    public ImmutableArray<FlowValue> GetArray()
        => GetValue<ImmutableArray<FlowValue>>(FlowValueKind.Array);

    public ImmutableDictionary<string, FlowValue> GetObject()
        => GetValue<ImmutableDictionary<string, FlowValue>>(FlowValueKind.Object);

    public bool Equals(FlowValue? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null || Kind != other.Kind)
            return false;

        return Kind switch
        {
            FlowValueKind.Null => true,
            FlowValueKind.Boolean => GetBoolean() == other.GetBoolean(),
            FlowValueKind.Integer => GetInteger() == other.GetInteger(),
            FlowValueKind.Decimal => GetDecimal() == other.GetDecimal(),
            FlowValueKind.FloatingPoint => GetFloatingPoint().Equals(other.GetFloatingPoint()),
            FlowValueKind.String => string.Equals(GetString(), other.GetString(), StringComparison.Ordinal),
            FlowValueKind.Binary => GetBinary().AsSpan().SequenceEqual(other.GetBinary().AsSpan()),
            FlowValueKind.DateTimeOffset => GetDateTimeOffset().EqualsExact(other.GetDateTimeOffset()),
            FlowValueKind.Date => GetDate() == other.GetDate(),
            FlowValueKind.Time => GetTime() == other.GetTime(),
            FlowValueKind.Duration => GetDuration() == other.GetDuration(),
            FlowValueKind.Guid => GetGuid() == other.GetGuid(),
            FlowValueKind.Array => ArrayEquals(GetArray(), other.GetArray()),
            FlowValueKind.Object => ObjectEquals(GetObject(), other.GetObject()),
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is FlowValue other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        switch (Kind)
        {
            case FlowValueKind.Null:
                break;
            case FlowValueKind.Boolean:
                hash.Add(GetBoolean());
                break;
            case FlowValueKind.Integer:
                hash.Add(GetInteger());
                break;
            case FlowValueKind.Decimal:
                hash.Add(GetDecimal());
                break;
            case FlowValueKind.FloatingPoint:
                hash.Add(GetFloatingPoint());
                break;
            case FlowValueKind.String:
                hash.Add(GetString(), StringComparer.Ordinal);
                break;
            case FlowValueKind.Binary:
                foreach (var item in GetBinary()) hash.Add(item);
                break;
            case FlowValueKind.DateTimeOffset:
                hash.Add(GetDateTimeOffset().Ticks);
                hash.Add(GetDateTimeOffset().Offset);
                break;
            case FlowValueKind.Date:
                hash.Add(GetDate());
                break;
            case FlowValueKind.Time:
                hash.Add(GetTime());
                break;
            case FlowValueKind.Duration:
                hash.Add(GetDuration());
                break;
            case FlowValueKind.Guid:
                hash.Add(GetGuid());
                break;
            case FlowValueKind.Array:
                foreach (var item in GetArray()) hash.Add(item);
                break;
            case FlowValueKind.Object:
                foreach (var property in GetObject().OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    hash.Add(property.Key, StringComparer.Ordinal);
                    hash.Add(property.Value);
                }
                break;
        }

        return hash.ToHashCode();
    }

    public override string ToString() => FlowValueCanonicalJson.Serialize(this);

    public static bool operator ==(FlowValue? left, FlowValue? right) => Equals(left, right);

    public static bool operator !=(FlowValue? left, FlowValue? right) => !Equals(left, right);

    public static implicit operator FlowValue(bool value) => From(value);

    public static implicit operator FlowValue(int value) => From(value);

    public static implicit operator FlowValue(long value) => From(value);

    public static implicit operator FlowValue(BigInteger value) => From(value);

    public static implicit operator FlowValue(decimal value) => From(value);

    public static implicit operator FlowValue(double value) => From(value);

    public static implicit operator FlowValue(string value) => From(value);

    private T GetValue<T>(FlowValueKind expectedKind)
    {
        if (Kind != expectedKind)
        {
            throw new InvalidOperationException(
                $"FlowValue kind '{Kind}' cannot be read as '{expectedKind}'.");
        }

        return (T)_value!;
    }

    private static bool ArrayEquals(
        ImmutableArray<FlowValue> left,
        ImmutableArray<FlowValue> right)
        => left.Length == right.Length && left.AsSpan().SequenceEqual(right.AsSpan());

    private static bool ObjectEquals(
        ImmutableDictionary<string, FlowValue> left,
        ImmutableDictionary<string, FlowValue> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var property in left)
        {
            if (!right.TryGetValue(property.Key, out var value) || property.Value != value)
                return false;
        }

        return true;
    }
}
