using System.Text.Json.Nodes;
using FluxFlow.Environments;

namespace FluxFlow.Plugins.Ros2;

public sealed class RosNodeEnvironmentAdapter : IResourceEnvironmentAdapter
{
    public string Type => "ros.node";
    public string? Endpoint(JsonObject resource) => null;
    public void Apply(JsonObject resource, Uri endpoint) =>
        throw new ArgumentException("ROS nodes use the execution host's ROS environment, not a service endpoint.", nameof(endpoint));
}
