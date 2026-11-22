using System.Text.Json;
using FluxFlow.Authoring;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Plugins.Ros2;

public static class RosNodeDescription
{
    public static PluginConnection Describe() => new("ros.node", "ROS node", null, false,
        JsonSerializer.SerializeToElement(new { Type = "ros.node", Name = "node" }),
        [Create("Name", "Node name", help: "Name used when the runner creates the ROS node.") with { IsRequired = true },
         Create("Namespace", "Namespace", defaultValue: "/") with { IsRequired = true }])
        { WorkflowResourcesAreLocal = true };
}
