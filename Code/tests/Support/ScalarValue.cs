global using FluxFlow.Testing;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Testing;

// Fixture payload for tests that previously sent raw CLR scalars. Not a Core API.
[JsonConverter(typeof(ScalarValueConverterFactory))]
public sealed class ScalarValue<T>(T data) : FlowValue
{
    public T Data { get; } = data;
    protected override JsonElement CreateJson() => JsonSerializer.SerializeToElement(Data);
    public static implicit operator T(ScalarValue<T> value) => value.Data;
    public static implicit operator ScalarValue<T>(T value) => new(value);
    public override string? ToString() => Data?.ToString();
    public override bool Equals(object? other) => other is ScalarValue<T> value
        ? EqualityComparer<T>.Default.Equals(Data, value.Data)
        : other is T typed && EqualityComparer<T>.Default.Equals(Data, typed);
    public override int GetHashCode() => Data is null ? 0 : EqualityComparer<T>.Default.GetHashCode(Data);
}

public sealed class ScalarValueConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(ScalarValue<>);
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(ScalarValueConverter<>).MakeGenericType(typeToConvert.GenericTypeArguments))!;
    private sealed class ScalarValueConverter<T> : JsonConverter<ScalarValue<T>>
    {
        public override ScalarValue<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(JsonSerializer.Deserialize<T>(ref reader, options)!);
        public override void Write(Utf8JsonWriter writer, ScalarValue<T> value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.Data, options);
    }
}
