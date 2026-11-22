using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using RosString = FluxFlow.Plugins.Ros2.Messages.Std.String;
using FluxFlow.Components.Ros2;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rcl;
using Rosidl.Runtime;

namespace FluxFlow.Configuration.Components.Ros2;

public static class RosRegistrationExtensions
{
    /// <summary>Registers deferred ros.node resources sharing one DI-owned context.</summary>
    public static IServiceCollection AddRosNode(this IServiceCollection services, string[]? arguments = null)
    {
        var args = arguments?.ToArray() ?? [];
        services.TryAddSingleton<IRclContext>(_ => new RclContext(args));
        services.AddKeyedSingleton<ResourceRegistration>("ros.node", (registrations, address, section) =>
        {
            var config = section.ReadRequiredConfiguration<RosNodeConfiguration>();
            registrations.AddRosNodeResource(address.Value, config);
        });
        return services;
    }

    /// <summary>Associates a configuration type key with one generated ROS message type.</summary>
    public static IServiceCollection AddRosPublisher<T>(this IServiceCollection services, string type = "ros.publisher")
        where T : class, IMessage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        services.AddKeyedSingleton<ComponentRegistration>(type, (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<RosPublisherConfiguration>();
            RegisterPublisher<T>(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        return services;
    }

    /// <summary>Associates a configuration type key with one generated ROS message type.</summary>
    public static IServiceCollection AddRosSubscriber<T>(this IServiceCollection services, string type = "ros.subscriber")
        where T : class, IMessage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        services.AddKeyedSingleton<ComponentRegistration>(type, (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<RosSubscriberConfiguration>();
            RegisterSubscriber<T>(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        return services;
    }

    public static IServiceCollection AddRosNodeResource(this IServiceCollection registrations, string path, RosNodeConfiguration config)
    {
        var address = ApplicationAddress.Parse(path);
        registrations.TryAddSingleton<IRclContext>(_ => new RclContext([]));
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Namespace);
        return registrations.AddFluxFlowResource<IRclNode>(address, provider =>
            provider.GetRequiredService<IRclContext>().CreateNode(config.Name, config.Namespace));
    }

    public static WorkflowBuilder RosPublisher(this WorkflowBuilder workflow, string name, RosPublisherConfiguration config)
    {
        RegisterPublisher<RosString>(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterPublisher<T>(IServiceCollection registrations, ComponentAddress address, RosPublisherConfiguration config) where T : class, IMessage
    {
        var publisher = config.CreateOptions();
        registrations.AddComponentFactory(address, provider => new RosPublisher<T>(new RosPublisherOptions<T>
        {
            Node = provider.GetRequiredFlowResource<IRclNode>(config.Node!, address.WorkflowName),
            Topic = config.Topic!,
            Publisher = publisher,
            OutputCapacity = config.OutputCapacity
        }))
            .AddInputPort<RosPublisher<T>>(address, "Input", node => node.Input)
            .AddOutputPort<RosPublisher<T>>(address, "Output", node => node.Output)
            .AddEventPort<RosPublisher<T>>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder RosSubscriber(this WorkflowBuilder workflow, string name, RosSubscriberConfiguration config)
    {
        RegisterSubscriber<RosString>(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterSubscriber<T>(IServiceCollection registrations, ComponentAddress address, RosSubscriberConfiguration config) where T : class, IMessage
    {
        var subscription = config.CreateOptions();
        registrations.AddComponentFactory(address, provider => new RosSubscriber<T>(new RosSubscriberOptions<T>
        {
            Node = provider.GetRequiredFlowResource<IRclNode>(config.Node!, address.WorkflowName),
            Topic = config.Topic!,
            Subscription = subscription,
            OutputCapacity = config.OutputCapacity
        }))
            .AddOutputPort<RosSubscriber<T>>(address, "Output", node => node.Output)
            .AddEventPort<RosSubscriber<T>>(address, "Events", node => node.Events);
    }
}
