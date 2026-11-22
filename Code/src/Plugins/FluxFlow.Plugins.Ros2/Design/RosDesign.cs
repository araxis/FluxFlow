using System.Threading.Channels;
using FluxFlow.Authoring.Definitions.RosSubscriber;
using FluxFlow.Environments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Ros2;

public sealed class RosDesign : IPluginDesign
{
    public void ConfigureRemoteServices(IServiceCollection services) =>
        services.AddSingleton<IResourceEnvironmentAdapter, RosNodeEnvironmentAdapter>();

    private static readonly PropertyObject Qos = new("Qos", "QoS", RosQos.Defaults, RosQos.Sections, RosQos.ValidateJson);
    public PropertyRules Component(PluginComponent component)
    {
        var subscriber = component.Palette.ComponentType == "ros.subscriber";
        return new()
        {
            NestedObject = Qos,
            Inspector = subscriber ? new RosSubscriberDescription().GetInspector : null,
            Validate = section =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(section["Node"], "Node");
                ArgumentException.ThrowIfNullOrWhiteSpace(section["Topic"], "Topic");
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(section.GetValue("OutputCapacity", 128), "OutputCapacity");
                _ = section.GetSection("Qos").Get<QosSettings>(options => options.ErrorOnUnknownConfiguration = true);
                if (!subscriber) return;
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(section.GetValue("QueueSize", 64), "QueueSize");
                _ = section.GetValue("IgnoreLocalPublications", false);
                var mode = section.GetValue("FullMode", BoundedChannelFullMode.DropOldest);
                if (!Enum.IsDefined(mode) || mode == BoundedChannelFullMode.Wait)
                    throw new ArgumentOutOfRangeException("FullMode", "The ROS delivery queue requires a drop mode.");
            },
            ValidationNote = _ => new("The runner requires native ROS and std_msgs type support. Node and topic names are checked by ROS when starting.")
        };
    }

    public PropertyRules Connection(PluginConnection connection) => new()
    {
        Validate = section =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(section["Name"], "Name");
            ArgumentException.ThrowIfNullOrWhiteSpace(section["Namespace"] ?? "/", "Namespace");
        }
    };
}
