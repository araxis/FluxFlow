using System.Threading.Channels;
using FluxFlow.Configuration.Components;
using Rcl.Qos;

namespace FluxFlow.Configuration.Components.Ros2;

public sealed record RosSubscriberConfiguration : OutputComponentConfiguration
{
    public string? Node { get; set; }
    public string? Topic { get; set; }
    public QosProfile? Qos { get; set; }
    public int QueueSize { get; set; } = 64;
    public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.DropOldest;
    public bool IgnoreLocalPublications { get; set; }
    public int OutputCapacity { get; set; } = 128;
}
