using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;
using Rosidl.Runtime;

namespace FluxFlow.Components.Ros2;

internal static class RosMessageJson<T> where T : class, IMessage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static T Read(FlowValue value) => value.Json.Deserialize<T>(Options)
        ?? throw new JsonException("A ROS message cannot be null.");

    internal static FlowValue Write(T message)
        => new JsonFlowValue(JsonSerializer.SerializeToElement(message, Options));
}
