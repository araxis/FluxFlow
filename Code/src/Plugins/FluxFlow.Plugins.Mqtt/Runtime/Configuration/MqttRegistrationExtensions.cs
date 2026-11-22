using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Components.Mqtt;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Mqtt;

public static class MqttRegistrationExtensions
{
    /// <summary>Registers mqtt.client resources and the three MQTT component registrars. Does not connect.</summary>
    public static IServiceCollection AddMqtt(this IServiceCollection services, string? baseDirectory = null)
    {
        var directory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        services.AddKeyedSingleton<ResourceRegistration>("mqtt.client", (registrations, address, section) =>
            MqttClientRegistration.Register(registrations, address, section, directory));

        services.AddKeyedSingleton<ComponentRegistration>("mqtt.connection", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            foreach (var setting in new[] { nameof(MqttClientConfiguration.AutoConnect), nameof(MqttClientConfiguration.Reconnect), "ReconnectInterval" })
                if (section.GetSection(setting).Exists())
                    throw new ArgumentException($"'{section.Path}:{setting}' belongs on the mqtt.client resource, not the connection component.");
            var config = section.ReadRequiredConfiguration<MqttComponentConfiguration>();
            RegisterMqttConnection(registrations, address, config);
            registrations.AddLinks(address, config);
        });

        services.AddKeyedSingleton<ComponentRegistration>("mqtt.publisher", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<MqttComponentConfiguration>();
            RegisterMqttPublisher(registrations, address, config);
            registrations.AddLinks(address, config);
        });

        services.AddKeyedSingleton<ComponentRegistration>("mqtt.subscriber", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<MqttSubscriberConfiguration>();
            config.Topics = MqttSubscriptions.ReadTopics(section.GetSection(nameof(config.Topics)));
            RegisterMqttSubscriber(registrations, address, config);
            registrations.AddLinks(address, config);
            registrations.AddInputLinks(address.Port("ACK"), config.ACK);
        });
        return services;
    }

    public static IServiceCollection AddMqttConnection(this IServiceCollection services, string address, MqttClientConfiguration config)
    {
        MqttClientRegistration.Register(services, ApplicationAddress.Parse(address), config, AppContext.BaseDirectory, config.Validate());
        return services;
    }

    public static WorkflowBuilder MqttConnection(this WorkflowBuilder workflow, string name, MqttComponentConfiguration config)
    {
        RegisterMqttConnection(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterMqttConnection(IServiceCollection registrations, ComponentAddress address, MqttComponentConfiguration config)
    {
        Validate(config);
        registrations.AddComponentFactory(address, provider => new MqttConnection(new()
        {
            Connection = provider.GetRequiredFlowResource<MqttClientResource>(config.Connection, address.WorkflowName),
            OutputCapacity = config.OutputCapacity
        }))
            .AddInputPort<MqttConnection>(address, "Input", node => node.Input)
            .AddOutputPort<MqttConnection>(address, "Output", node => node.Output)
            .AddEventPort<MqttConnection>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder MqttPublisher(this WorkflowBuilder workflow, string name, MqttComponentConfiguration config)
    {
        RegisterMqttPublisher(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterMqttPublisher(IServiceCollection registrations, ComponentAddress address, MqttComponentConfiguration config)
    {
        Validate(config);
        registrations.AddComponentFactory(address, provider => new MqttPublisher(new()
        {
            Connection = provider.GetRequiredFlowResource<MqttClientResource>(config.Connection, address.WorkflowName),
            OutputCapacity = config.OutputCapacity
        }))
            .AddInputPort<MqttPublisher>(address, "Input", node => node.Input)
            .AddOutputPort<MqttPublisher>(address, "Output", node => node.Output)
            .AddEventPort<MqttPublisher>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder MqttSubscriber(this WorkflowBuilder workflow, string name, MqttSubscriberConfiguration config)
    {
        RegisterMqttSubscriber(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterMqttSubscriber(IServiceCollection registrations, ComponentAddress address, MqttSubscriberConfiguration config)
    {
        Validate(config);
        foreach (var topic in config.Topics) topic.Validate();
        MqttSubscriptions.Validate(config.AcknowledgementMode, config.MaxPendingAcknowledgements, config.Topics);
        registrations.AddComponentFactory(address, provider => new MqttSubscriber(new()
        {
            Connection = provider.GetRequiredFlowResource<MqttClientResource>(config.Connection, address.WorkflowName),
            Topics = config.Topics, OutputCapacity = config.OutputCapacity,
            AcknowledgementMode = config.AcknowledgementMode, MaxPendingAcknowledgements = config.MaxPendingAcknowledgements
        }))
            .AddInputPort<MqttSubscriber>(address, "Input", node => node.Input)
            .AddInputPort<MqttSubscriber>(address, "ACK", node => node.ACK)
            .AddOutputPort<MqttSubscriber>(address, "Output", node => node.Output)
            .AddEventPort<MqttSubscriber>(address, "Events", node => node.Events);
    }

    private static void Validate(MqttComponentConfiguration config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Connection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.OutputCapacity);
    }
}
