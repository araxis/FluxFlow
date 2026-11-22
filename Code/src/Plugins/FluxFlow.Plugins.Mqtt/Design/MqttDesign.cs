using FluxFlow.Components.Mqtt;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Environments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Mqtt;

public sealed class MqttDesign : IPluginDesign
{
    public PropertyRules Component(PluginComponent component) => new()
    {
        Collection = component.Palette.ComponentType == "mqtt.subscriber" ? new MqttTopics() : null,
        Validate = section =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(section["Connection"]);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(section.GetValue("OutputCapacity", 128));
            if (component.Palette.ComponentType == "mqtt.subscriber")
                MqttSubscriptions.Validate(section.GetValue("AcknowledgementMode", MqttAcknowledgementMode.Automatic),
                    section.GetValue("MaxPendingAcknowledgements", 128), MqttSubscriptions.ReadTopics(section.GetSection("Topics")));
            if (component.Palette.ComponentType == "mqtt.connection")
                foreach (var key in new[] { "AutoConnect", "Reconnect", "ReconnectInterval" })
                    if (section.GetSection(key).Exists()) throw new ArgumentException($"{key} belongs on the mqtt.client resource.");
        }
    };
    public PropertyRules Connection(PluginConnection connection) => MqttConnectionProperties.Rules;
    public void ConfigureRemoteServices(IServiceCollection services) => services.AddSingleton<IResourceEnvironmentAdapter, MqttEnvironmentAdapter>()
        .AddSingleton(MosquittoSetup.Provider)
        .AddSingleton(RabbitMqMqttSetup.Provider);
}
