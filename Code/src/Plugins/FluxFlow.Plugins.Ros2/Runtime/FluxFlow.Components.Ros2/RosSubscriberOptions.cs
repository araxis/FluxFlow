using FluxFlow.Core.Messaging;
using Rcl;
using Rosidl.Runtime;

namespace FluxFlow.Components.Ros2;

public sealed class RosSubscriberOptions<T> where T : class, IMessage
{
    /// <summary>Borrowed node; its owner must keep it alive until the component is disposed.</summary>
    public required IRclNode Node { get; init; }
    public required string Topic { get; init; }
    public SubscriptionOptions Subscription { get; init; } = SubscriptionOptions.Default;
    /// <summary>Must return an immutable value. The default snapshots ROS properties as JSON.</summary>
    public Func<T, FlowValue> ToValue { get; init; } = RosMessageJson<T>.Write;
    public int OutputCapacity { get; init; } = 128;
}
