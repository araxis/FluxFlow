using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.RabbitConsumer;
using FluxFlow.Authoring.Definitions.RabbitPublisher;
using FluxFlow.Authoring.Definitions.RabbitSettlement;
using FluxFlow.Configuration.Components.RabbitMQ;
using FluxFlow.Environments;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.RabbitMQ;

public sealed class RabbitMQPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = new("FluxFlow.Plugins.RabbitMQ",
        [Component(new RabbitPublisherDescription(), "properties"), Component(new RabbitConsumerDescription(), "properties"),
         Component(new RabbitSettlementDescription())], [AmqpConnectionSchema.Describe()])
        { Design = new(typeof(RabbitDesign).Assembly.GetName().Name!, typeof(RabbitDesign).FullName!,
            [typeof(RabbitConnectionConfiguration).Assembly.GetName().Name!]) };

    private static PluginComponent Component(IComponentDescription description, string? editor = null) =>
        new(description.Palette, description.Inspector, description.Documentation, editor);

    public void ConfigureWorkflow(IServiceCollection services) => services.AddRabbitMQ();
    public void ConfigureHost(IServiceCollection services)
    {
        new RabbitDesign().ConfigureRemoteServices(services);
        services.AddSingleton(RabbitMqAmqpContainer.Definition);
    }
}
