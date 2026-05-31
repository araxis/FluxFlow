using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Mqtt.Options;

internal static class MqttClientFactoryContexts
{
    public static MqttClientFactoryContext Create(
        RuntimeNodeFactoryContext context,
        MqttPublishOptions options)
        => Create(context, options.ConnectionName, options.Connection);

    public static MqttClientFactoryContext Create(
        RuntimeNodeFactoryContext context,
        MqttSubscriptionOptions options)
        => Create(context, options.ConnectionName, options.Connection);

    private static MqttClientFactoryContext Create(
        RuntimeNodeFactoryContext context,
        string? connectionName,
        MqttConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);

        return new MqttClientFactoryContext
        {
            Address = context.Address,
            ConnectionName = string.IsNullOrWhiteSpace(connectionName)
                ? profile.Name
                : connectionName,
            Profile = profile
        };
    }
}
