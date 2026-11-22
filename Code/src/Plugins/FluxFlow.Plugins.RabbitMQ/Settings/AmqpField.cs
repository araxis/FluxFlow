using System.Globalization;
using System.Text.Json;

namespace FluxFlow.Components.RabbitMQ;

/// <summary>Lossless JSON representation of the field types accepted by the native client.</summary>
public sealed record AmqpField(string Type, JsonElement Value)
{
    public object? Decode(Func<long, object> timestamp) => Type switch
    {
        "Null" => null,
        "Bytes" => Convert.FromBase64String(Value.GetString()!),
        "String" => Value.GetString(),
        "Boolean" => Value.GetBoolean(),
        "Byte" => Value.GetByte(),
        "SByte" => Value.GetSByte(),
        "Int16" => Value.GetInt16(),
        "UInt16" => Value.GetUInt16(),
        "Int32" => Value.GetInt32(),
        "UInt32" => Value.GetUInt32(),
        "Int64" => long.Parse(Value.GetString()!, CultureInfo.InvariantCulture),
        "Single" => float.Parse(Value.GetString()!, CultureInfo.InvariantCulture),
        "Double" => double.Parse(Value.GetString()!, CultureInfo.InvariantCulture),
        "Decimal" => decimal.Parse(Value.GetString()!, CultureInfo.InvariantCulture),
        "Timestamp" => timestamp(long.Parse(Value.GetString()!, CultureInfo.InvariantCulture)),
        "Array" => Value.Deserialize<AmqpField[]>(FieldJson)!.Select(field => field.Decode(timestamp)).ToList(),
        "Table" => Value.Deserialize<Dictionary<string, AmqpField>>(FieldJson)!.ToDictionary(pair => { RabbitTopology.Name(pair.Key); return pair.Key; }, pair => pair.Value.Decode(timestamp)),
        _ => throw new ArgumentException($"Unknown AMQP field type '{Type}'.")
    };
    private static readonly JsonSerializerOptions FieldJson = new() { PropertyNameCaseInsensitive = true };
}
