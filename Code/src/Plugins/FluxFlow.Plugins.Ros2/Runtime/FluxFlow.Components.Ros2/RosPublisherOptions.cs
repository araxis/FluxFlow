using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Rcl;
using Rosidl.Runtime;

namespace FluxFlow.Components.Ros2;

public sealed class RosPublisherOptions<T> where T : class, IMessage
{
    /// <summary>Borrowed node; its owner must keep it alive until the component is disposed.</summary>
    public required IRclNode Node { get; init; }
    public required string Topic { get; init; }
    public PublisherOptions Publisher { get; init; } = PublisherOptions.Default;
    public Func<FlowValue, T> ToMessage { get; init; } = RosMessageJson<T>.Read;
    public ExecutionDataflowBlockOptions? Execution { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
