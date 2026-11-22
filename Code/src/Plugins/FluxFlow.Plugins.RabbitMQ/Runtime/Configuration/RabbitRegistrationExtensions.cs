using FluxFlow.Application;
using FluxFlow.Components.RabbitMQ;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Configuration.Components.RabbitMQ;

public static class RabbitRegistrationExtensions
{
    public static IServiceCollection AddRabbitMQ(this IServiceCollection services)
    {
        foreach (var type in new[] { "amqp.publisher", "amqp.consumer" })
            services.AddKeyedSingleton<ComponentConfigurationCheck>(type, (application, component) =>
            {
                var address = component.GetComponentAddress();
                var reference = component["Connection"];
                ArgumentException.ThrowIfNullOrWhiteSpace(reference);
                var resource = ApplicationAddress.ResolveResourcePaths(reference, address.WorkflowName)
                    .Select(path => application.GetSection((path.WorkflowName is null ? "" : "Workflows:") + string.Join(":", path.Segments)))
                    .FirstOrDefault(section => section.Exists());
                if (resource?["Type"] != "amqp.connection") throw new ArgumentException($"'{reference}' must name an amqp.connection resource.");
                RabbitConnectionConfiguration.Read(resource);
            });
        services.AddKeyedSingleton<ComponentConfigurationCheck>("amqp.settlement", (application, component) =>
        {
            var address = component.GetComponentAddress();
            var name = component["Consumer"];
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var consumer = ComponentAddress.Create(address.WorkflowName, name);
            if (application.GetSection($"Workflows:{consumer.WorkflowName}:{consumer.ComponentName}")["Type"] != "amqp.consumer")
                throw new ArgumentException($"'{consumer}' must name an AMQP consumer in this workflow.");
        });
        services.AddKeyedSingleton<ResourceRegistration>("amqp.connection", (registrations, address, section) =>
        {
            var config = RabbitConnectionConfiguration.Read(section);
            registrations.AddRabbitConnection(address.Value, config);
        });
        services.AddKeyedSingleton<ComponentRegistration>("amqp.publisher", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = RabbitComponentConfiguration.Read(section);
            RegisterRabbitPublisher(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        services.AddKeyedSingleton<ComponentRegistration>("amqp.consumer", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = RabbitComponentConfiguration.Read(section);
            RegisterRabbitConsumer(registrations, address, config);
            registrations.AddLinks(address, config);
            registrations.AddInputLinks(address.Port("ACK"), config.ACK);
            registrations.AddInputLinks(address.Port("NACK"), config.NACK);
        });
        services.AddKeyedSingleton<ComponentRegistration>("amqp.settlement", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<RabbitSettlementConfiguration>();
            RegisterRabbitSettlement(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        return services;
    }

    public static IServiceCollection AddRabbitConnection(this IServiceCollection registrations, string address, RabbitConnectionConfiguration config)
    {
        config.Validate();
        return registrations.AddFluxFlowResource(ApplicationAddress.Parse(address), _ => new RabbitConnection(config.CreateFactory()));
    }

    public static WorkflowBuilder RabbitPublisher(this WorkflowBuilder workflow, string name, RabbitComponentConfiguration config)
    {
        RegisterRabbitPublisher(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterRabbitPublisher(IServiceCollection services, ComponentAddress address, RabbitComponentConfiguration config)
    {
        config.Validate(false);
        services.AddComponentFactory(address, provider => new RabbitPublisher(
                provider.GetRequiredFlowResource<RabbitConnection>(config.Connection, address.WorkflowName),
                config.Topology, config.Capacity, config.MaxMessageBytes, config.TimeoutSeconds))
            .AddInputPort<RabbitPublisher>(address, "Input", node => node.Input)
            .AddOutputPort<RabbitPublisher>(address, "Output", node => node.Output)
            .AddEventPort<RabbitPublisher>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder RabbitConsumer(this WorkflowBuilder workflow, string name, RabbitComponentConfiguration config)
    {
        RegisterRabbitConsumer(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterRabbitConsumer(IServiceCollection services, ComponentAddress address, RabbitComponentConfiguration config)
    {
        config.Validate(true);
        services.AddComponentFactory(address, provider => new RabbitConsumer(
                provider.GetRequiredFlowResource<RabbitConnection>(config.Connection, address.WorkflowName), config.ConsumerOptions()))
            .AddInputPort<RabbitConsumer>(address, "Input", node => node.Input)
            .AddInputPort<RabbitConsumer>(address, "ACK", node => node.ACK)
            .AddInputPort<RabbitConsumer>(address, "NACK", node => node.NACK)
            .AddOutputPort<RabbitConsumer>(address, "Output", node => node.Output)
            .AddEventPort<RabbitConsumer>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder RabbitSettlement(this WorkflowBuilder workflow, string name, RabbitSettlementConfiguration config)
    {
        RegisterRabbitSettlement(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterRabbitSettlement(IServiceCollection services, ComponentAddress address, RabbitSettlementConfiguration config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Consumer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.Capacity);
        var consumer = ComponentAddress.Create(address.WorkflowName, config.Consumer);
        if (consumer == address) throw new ArgumentException("Settlement must reference an AMQP consumer in the same workflow.");
        services.AddComponentFactory(address, provider => new RabbitSettlement(
                provider.GetRequiredKeyedService<IFlowNode>(consumer.Value) as RabbitConsumer
                    ?? throw new ArgumentException($"'{consumer}' must be an AMQP consumer."), config.Capacity))
            .AddInputPort<RabbitSettlement>(address, "Input", node => node.Input)
            .AddOutputPort<RabbitSettlement>(address, "Output", node => node.Output)
            .AddEventPort<RabbitSettlement>(address, "Events", node => node.Events);
    }
}
