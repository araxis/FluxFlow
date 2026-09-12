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
            : value.Deserialize<T>()!);
    }
}
