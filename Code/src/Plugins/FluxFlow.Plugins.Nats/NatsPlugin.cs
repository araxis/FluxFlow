using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.NatsPublisher;
using FluxFlow.Authoring.Definitions.NatsSubscriber;
using FluxFlow.Authoring.Definitions.NatsRequest;
using FluxFlow.Authoring.Definitions.NatsReply;
using FluxFlow.Authoring.Definitions.JetStreamPublisher;
using FluxFlow.Authoring.Definitions.JetStreamConsumer;
using FluxFlow.Authoring.Definitions.JetStreamSettlement;
using FluxFlow.Configuration.Components.Nats;
using FluxFlow.Environments;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Nats;

public sealed class NatsPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = new("FluxFlow.Plugins.Nats",
        [Component(new NatsPublisherDescription()),
         Component(new NatsSubscriberDescription()),
         Component(new NatsRequestDescription()),
         Component(new NatsReplyDescription()),
         Component(new JetStreamPublisherDescription()),
         Component(new JetStreamConsumerDescription()),
         Component(new JetStreamSettlementDescription())],
        [new("nats.connection", "NATS", "Endpoint", false, JsonSerializer.SerializeToElement(new { Type = "nats.connection", Endpoint = "nats://localhost:4222" }), [])])
        { Design = new(typeof(NatsDesign).Assembly.GetName().Name!, typeof(NatsDesign).FullName!,
            [typeof(NatsConnectionConfiguration).Assembly.GetName().Name!]) };
    private static PluginComponent Component(IComponentDescription description) =>
        new(description.Palette, description.Inspector, description.Documentation, "properties");
    public void ConfigureWorkflow(IServiceCollection services) => services.AddNats();
    public void ConfigureHost(IServiceCollection services)
    {
        new NatsDesign().ConfigureRemoteServices(services);
        services.AddSingleton(NatsContainer.Definition);
    }
}
