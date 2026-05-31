using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Mqtt;

public static class MqttComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterMqttComponents(
        this RuntimeNodeFactoryRegistry registry,
        IMqttClientFactory clientFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(clientFactory);

        return registry.Register(new MqttComponentModule(clientFactory));
    }

    public static RuntimeNodeFactoryRegistry RegisterMqttComponents(
        this RuntimeNodeFactoryRegistry registry,
        Func<MqttConnectionProfile, CancellationToken, ValueTask<IMqttClientAdapter>> clientFactory)
        => registry.RegisterMqttComponents(options => options.UseClientFactory(clientFactory));

    public static RuntimeNodeFactoryRegistry RegisterMqttComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<MqttComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MqttComponentOptions();
        configure(options);
        return registry.Register(new MqttComponentModule(options));
    }
}
