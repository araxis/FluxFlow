namespace FluxFlow.Configuration.Components.Ros2;

/// <summary>Settings for a shared ROS node resource; context arguments belong to host registration.</summary>
public sealed record RosNodeConfiguration
{
    public string? Name { get; set; }
    public string Namespace { get; set; } = "/";
}
