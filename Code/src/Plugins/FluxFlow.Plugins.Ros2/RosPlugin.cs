using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.RosPublisher;
using FluxFlow.Authoring.Definitions.RosSubscriber;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Ros2;
using Microsoft.Extensions.DependencyInjection;
using RosString = FluxFlow.Plugins.Ros2.Messages.Std.String;

namespace FluxFlow.Plugins.Ros2;

public sealed class RosPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = new("FluxFlow.Plugins.Ros2",
        [Describe(new RosPublisherDescription()), Describe(new RosSubscriberDescription())],
        [RosNodeDescription.Describe()])
    {
        Design = new(typeof(RosDesign).Assembly.GetName().Name!, typeof(RosDesign).FullName!, [])
    };

    public void ConfigureWorkflow(IServiceCollection services)
    {
        services.AddRosNode();
        // Custom hosts may already associate these keys with their own generated message type.
        if (!Registered(services, "ros.publisher")) services.AddRosPublisher<RosString>();
        if (!Registered(services, "ros.subscriber")) services.AddRosSubscriber<RosString>();
    }

    public void ConfigureHost(IServiceCollection services) => new RosDesign().ConfigureRemoteServices(services);

    private static bool Registered(IServiceCollection services, string type) => services.Any(service =>
        service.IsKeyedService && service.ServiceType == typeof(ComponentRegistration) && Equals(service.ServiceKey, type));

    private static PluginComponent Describe(IComponentDescription component) =>
        new(component.Palette, component.Inspector, component.Documentation, "properties")
        { HostRequirement = "ROS 2 requires the native ROS runtime and std_msgs type-support libraries on the execution host." };
}
