using System.Globalization;
using System.Text.Json;
using RabbitMQ.Client;

namespace FluxFlow.Components.RabbitMQ;

/// <summary>Converts between portable AMQP field values and native RabbitMQ client values.</summary>
public static class AmqpWireField
{
    public static object? ToNative(this AmqpField field) => field.Decode(static seconds => new AmqpTimestamp(seconds));

    public static AmqpField FromNative(object? value) => value switch
    {
        null => Create("Null", (string?)null),
        byte[] bytes => Create("Bytes", Convert.ToBase64String(bytes)),
        ReadOnlyMemory<byte> bytes => Create("Bytes", Convert.ToBase64String(bytes.Span)),
        string text => Create("String", text),
        bool number => Create("Boolean", number),
        byte number => Create("Byte", number),
        sbyte number => Create("SByte", number),
        short number => Create("Int16", number),
        ushort number => Create("UInt16", number),
        int number => Create("Int32", number),
        uint number => Create("UInt32", number),
        long number => Create("Int64", number.ToString(CultureInfo.InvariantCulture)),
        float number => Create("Single", number.ToString("R", CultureInfo.InvariantCulture)),
        double number => Create("Double", number.ToString("R", CultureInfo.InvariantCulture)),
        decimal number => Create("Decimal", number.ToString(CultureInfo.InvariantCulture)),
        AmqpTimestamp stamp => Create("Timestamp", stamp.UnixTime.ToString(CultureInfo.InvariantCulture)),
        IDictionary<string, object?> table => Create("Table", table.ToDictionary(pair => pair.Key, pair => FromNative(pair.Value))),
        System.Collections.IList list => Create("Array", list.Cast<object?>().Select(FromNative).ToArray()),
        _ => throw new ArgumentException($"Unsupported AMQP field type {value.GetType().Name}.")
    };

    private static AmqpField Create<T>(string type, T value) => new(type, JsonSerializer.SerializeToElement(value, AmqpJson.Options));
}

internal static class AmqpJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public static T Read<T>(JsonElement value) => value.Deserialize<T>(Options) ?? throw new ArgumentException($"Expected {typeof(T).Name}.");
    public static FluxFlow.Core.Messaging.JsonFlowValue Value<T>(T value) => new(JsonSerializer.SerializeToElement(value, Options));
}
