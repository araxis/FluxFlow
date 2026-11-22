using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Components.Nats;
using FluxFlow.Components.Nats.JetStream;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Nats;

public static class JetStreamRegistration
{
    internal static IServiceCollection AddJetStream(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ComponentRegistration>("nats.jetstream.publisher", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<JetStreamConfiguration>();
            RegisterJetStreamPublisher(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        services.AddKeyedSingleton<ComponentRegistration>("nats.jetstream.consumer", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<JetStreamConfiguration>();
            RegisterJetStreamConsumer(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        services.AddKeyedSingleton<ComponentConfigurationCheck>("nats.jetstream.settlement", (application, component) =>
        {
            var address = component.GetComponentAddress();
            var config = component.ReadRequiredConfiguration<JetStreamSettlementConfiguration>(); config.Validate();
            var consumer = ComponentAddress.Create(address.WorkflowName, config.Consumer);
            if (application.GetSection($"Workflows:{consumer.WorkflowName}:{consumer.ComponentName}")["Type"] != "nats.jetstream.consumer")
                throw new ArgumentException($"'{consumer}' must name a JetStream consumer in this workflow.");
        });
        services.AddKeyedSingleton<ComponentRegistration>("nats.jetstream.settlement", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<JetStreamSettlementConfiguration>();
            RegisterJetStreamSettlement(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        return services;
    }

    public static WorkflowBuilder JetStreamPublisher(this WorkflowBuilder workflow, string name, JetStreamConfiguration config)
    {
        RegisterJetStreamPublisher(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterJetStreamPublisher(IServiceCollection services, ComponentAddress address, JetStreamConfiguration config)
    {
        var options = config.StreamOptions();
        services.AddComponentFactory(address, provider => new JetStreamPublisher(provider.GetRequiredFlowResource<NatsSession>(config.Connection, address.WorkflowName), options))
            .AddInputPort<JetStreamPublisher>(address, "Input", node => node.Input).AddOutputPort<JetStreamPublisher>(address, "Output", node => node.Output).AddEventPort<JetStreamPublisher>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder JetStreamConsumer(this WorkflowBuilder workflow, string name, JetStreamConfiguration config)
    {
        RegisterJetStreamConsumer(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterJetStreamConsumer(IServiceCollection services, ComponentAddress address, JetStreamConfiguration config)
    {
        var options = config.ConsumerOptions();
        services.AddComponentFactory(address, provider => new JetStreamConsumer(provider.GetRequiredFlowResource<NatsSession>(config.Connection, address.WorkflowName), options))
            .AddInputPort<JetStreamConsumer>(address, "Input", node => node.Input).AddOutputPort<JetStreamConsumer>(address, "Output", node => node.Output).AddEventPort<JetStreamConsumer>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder JetStreamSettlement(this WorkflowBuilder workflow, string name, JetStreamSettlementConfiguration config)
    {
        RegisterJetStreamSettlement(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterJetStreamSettlement(IServiceCollection services, ComponentAddress address, JetStreamSettlementConfiguration config)
    {
        config.Validate();
        var consumer = ComponentAddress.Create(address.WorkflowName, config.Consumer);
        if (consumer == address) throw new ArgumentException("Settlement must reference a JetStream consumer in this workflow.");
        services.AddComponentFactory(address, provider => new JetStreamSettlementNode(
                provider.GetRequiredKeyedService<IFlowNode>(consumer.Value) as JetStreamConsumer ?? throw new ArgumentException($"'{consumer}' must be a JetStream consumer."), config.Capacity))
            .AddInputPort<JetStreamSettlementNode>(address, "Input", node => node.Input).AddOutputPort<JetStreamSettlementNode>(address, "Output", node => node.Output).AddEventPort<JetStreamSettlementNode>(address, "Events", node => node.Events);
    }
}
