using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal static class ApplicationMessageProjection
{
    internal static FlowMessage<T> ToTyped<T>(FlowMessage message)
    {
        if (message.IsError)
            return message.ConvertError<T>();

        var value = message.Value!;
        return message.ConvertValue(typeof(T) == typeof(FlowValue)
            ? (T)(object)value
            : (value is T typedInput0 ? typedInput0 : typeof(T) == typeof(JsonElement) ? (T)(object)value.Json : value.Json.Deserialize<T>(ValueJsonOptions))!);
    }
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}
