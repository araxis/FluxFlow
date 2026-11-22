using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Expressions.CSharp;

/// <summary>Read-only expression access to owned JSON and message headers; no recursive object copy.</summary>
internal sealed class ExpressionValue : DynamicObject
{
    private readonly JsonElement _json;
    private readonly IReadOnlyDictionary<string, FlowValue>? _headers;
    private ExpressionValue(JsonElement json) => _json = json;
    internal ExpressionValue(IReadOnlyDictionary<string, FlowValue> headers) => _headers = headers;
    internal static readonly JsonSerializerOptions SerializerOptions = new() { Converters = { new Converter() } };

    internal static object? Read(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Object or JsonValueKind.Array => new ExpressionValue(json),
        JsonValueKind.String => json.GetString(),
        JsonValueKind.Number => Number(json),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new ArgumentException("Undefined JSON cannot be used as an expression value.")
    };

    private static object Number(JsonElement json)
    {
        if (json.TryGetInt64(out var integer)) return integer;
        if (json.TryGetDecimal(out var precise)) return precise;
        var number = json.GetDouble();
        if (!double.IsFinite(number)) throw new OverflowException("JSON number exceeds the finite Double range.");
        return number;
    }

    private object? Property(string name)
        => Read(_headers is null ? _json.GetProperty(name) : _headers[name].Json);

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        result = Property(binder.Name);
        return true;
    }

    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object? result)
    {
        if (indexes.Length != 1) throw new ArgumentException("Expected one property name or array index.");
        result = indexes[0] switch
        {
            string name => Property(name),
            int index when _headers is null => Read(_json[index]),
            long index when _headers is null => Read(_json[checked((int)index)]),
            decimal index when _headers is null && decimal.Truncate(index) == index => Read(_json[checked((int)index)]),
            _ => throw new ArgumentException("Expected a property name or array index.")
        };
        return true;
    }

    private sealed class Converter : JsonConverter<ExpressionValue>
    {
        public override ExpressionValue Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, ExpressionValue value, JsonSerializerOptions options)
        {
            if (value._headers is null) { value._json.WriteTo(writer); return; }
            writer.WriteStartObject();
            foreach (var (name, header) in value._headers)
            {
                writer.WritePropertyName(name);
                header.Json.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
    }
}
