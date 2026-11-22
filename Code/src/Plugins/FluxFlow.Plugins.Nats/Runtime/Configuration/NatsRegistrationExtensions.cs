using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Components.Nats;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Configuration.Components.Nats;

public static class NatsRegistrationExtensions
{
    public static IServiceCollection AddNats(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ResourceRegistration>("nats.connection", (registrations, address, section) =>
        {
            var settings = NatsConnectionConfiguration.Read(section);
            registrations.AddNatsConnection(address.Value, settings);
        });
        foreach (var type in new[] { "nats.publisher", "nats.subscriber", "nats.request", "nats.reply", "nats.jetstream.publisher", "nats.jetstream.consumer" })
            services.AddKeyedSingleton<ComponentConfigurationCheck>(type, (application, component) =>
            {
                var address = component.GetComponentAddress();
                var reference = component["Connection"]; ArgumentException.ThrowIfNullOrWhiteSpace(reference);
                var resource = ApplicationAddress.ResolveResourcePaths(reference, address.WorkflowName)
                    .Select(path => application.GetSection((path.WorkflowName is null ? "" : "Workflows:") + string.Join(":", path.Segments)))
                    .FirstOrDefault(section => section.Exists());
                if (resource?["Type"] != "nats.connection") throw new ArgumentException($"'{reference}' must name a nats.connection resource.");
                NatsConnectionConfiguration.Read(resource);
            });
        RegisterCommand(services, "nats.publisher", CreatePublisher);
        RegisterCommand(services, "nats.request", CreateRequest);
        RegisterCommand(services, "nats.reply", CreateReply);
        services.AddKeyedSingleton<ComponentRegistration>("nats.subscriber", (registrations, section) =>
        {
            if (section.GetSection("Input").Exists()) throw new ArgumentException("NATS Subscriber is a source and has no Input port.");
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<NatsSubscriberConfiguration>();
            RegisterNatsSubscriber(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        return services.AddJetStream();
    }
    public static IServiceCollection AddNatsConnection(this IServiceCollection registrations, string address, NatsConnectionConfiguration settings)
    {
        settings.Validate();
        return registrations.AddFluxFlowResource(ApplicationAddress.Parse(address),
            provider => new NatsSession(settings.CreateOptions(provider.GetRequiredService<ILoggerFactory>())));
    }

    private static void RegisterCommand<T>(IServiceCollection services, string type,
        Func<NatsSession, NatsComponentOptions, T> create) where T : NatsCommand
    {
        services.AddKeyedSingleton<ComponentRegistration>(type, (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<NatsCommandConfiguration>();
            AddCommand(registrations, address, config, create);
            registrations.AddLinks(address, config);
        });
    }

    public static WorkflowBuilder NatsSubscriber(this WorkflowBuilder workflow, string name, NatsSubscriberConfiguration config)
    {
        RegisterNatsSubscriber(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterNatsSubscriber(IServiceCollection services, ComponentAddress address, NatsSubscriberConfiguration config)
    {
        var options = config.Options();
        NatsComponentOptions.ValidateSubject(config.Subject, true);
        if (config.QueueGroup is not null) NatsComponentOptions.ValidateSubject(config.QueueGroup);
        services.AddComponentFactory(address, provider => new NatsSubscriber(provider.GetRequiredFlowResource<NatsSession>(config.Connection, address.WorkflowName), config.Subject, config.QueueGroup, options))
            .AddOutputPort<NatsSubscriber>(address, "Output", node => node.Output)
            .AddEventPort<NatsSubscriber>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder NatsPublisher(this WorkflowBuilder workflow, string name, NatsCommandConfiguration config)
    {
        AddCommand(workflow.Services, workflow.Address(name), config, CreatePublisher);
        return workflow;
    }

    private static NatsPublisher CreatePublisher(NatsSession session, NatsComponentOptions options) => new(session, options);

    public static WorkflowBuilder NatsRequest(this WorkflowBuilder workflow, string name, NatsCommandConfiguration config)
    {
        AddCommand(workflow.Services, workflow.Address(name), config, CreateRequest);
        return workflow;
    }

    private static NatsRequest CreateRequest(NatsSession session, NatsComponentOptions options) => new(session, options);

    public static WorkflowBuilder NatsReply(this WorkflowBuilder workflow, string name, NatsCommandConfiguration config)
    {
        AddCommand(workflow.Services, workflow.Address(name), config, CreateReply);
        return workflow;
    }

    private static NatsReply CreateReply(NatsSession session, NatsComponentOptions options) => new(session, options);

    private static void AddCommand<T>(IServiceCollection services, ComponentAddress address, NatsCommandConfiguration config,
        Func<NatsSession, NatsComponentOptions, T> create) where T : NatsCommand
    {
        var options = config.Options();
        services.AddComponentFactory(address, provider =>
                create(provider.GetRequiredFlowResource<NatsSession>(config.Connection, address.WorkflowName), options))
            .AddInputPort<T>(address, "Input", node => node.Input)
            .AddOutputPort<T>(address, "Output", node => node.Output)
            .AddEventPort<T>(address, "Events", node => node.Events);
    }
}
