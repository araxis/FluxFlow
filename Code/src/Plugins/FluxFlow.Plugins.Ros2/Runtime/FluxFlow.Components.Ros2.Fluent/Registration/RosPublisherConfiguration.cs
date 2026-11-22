using FluxFlow.Configuration.Components;
using Rcl.Qos;

namespace FluxFlow.Configuration.Components.Ros2;

public sealed record RosPublisherConfiguration : ComponentConfiguration
{
    public string? Node { get; set; }
    public string? Topic { get; set; }
    public QosProfile? Qos { get; set; }
    public int OutputCapacity { get; set; } = 128;
}
