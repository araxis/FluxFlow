using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.MqttConnection;
using FluxFlow.Authoring.Definitions.MqttPublisher;
using FluxFlow.Authoring.Definitions.MqttSubscriber;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Environments;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Mqtt;

public sealed class MqttPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = new("FluxFlow.Plugins.Mqtt",
        [Component(new MqttPublisherDescription()), Component(new MqttSubscriberDescription(), "properties"),
         Component(new MqttConnectionDescription())], [MqttClientSchema.Describe()]) { Design = new(typeof(MqttDesign).Assembly.GetName().Name!, typeof(MqttDesign).FullName!, ["Polly.Core", typeof(MqttClientConfiguration).Assembly.GetName().Name!]) };

    private static PluginComponent Component(IComponentDescription description, string? editor = null) =>
        new(description.Palette, description.Inspector, description.Documentation, editor);

    public void ConfigureWorkflow(IServiceCollection services) => services.AddMqtt();
    public void ConfigureHost(IServiceCollection services)
    {
        new MqttDesign().ConfigureRemoteServices(services);
        services.AddSingleton(MosquittoContainer.Definition);
        services.AddSingleton(RabbitMqMqttContainer.Definition);
    }
}
