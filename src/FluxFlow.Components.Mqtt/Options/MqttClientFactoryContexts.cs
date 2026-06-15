using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Mqtt.Options;

internal static class MqttClientFactoryContexts
{
    public static MqttClientFactoryContext Create(
        RuntimeNodeFactoryContext context,
        MqttPublishOptions options,
        TimeProvider clock)
        => Create(context, options.ConnectionName, options.Connection, options.Reconnect, clock);

    public static MqttClientFactoryContext Create(
        RuntimeNodeFactoryContext context,
        MqttSubscriptionOptions options,
        TimeProvider clock)
        => Create(context, options.ConnectionName, options.Connection, options.Reconnect, clock);

    private static MqttClientFactoryContext Create(
        RuntimeNodeFactoryContext context,
        string? connectionName,
        MqttConnectionProfile profile,
        MqttReconnectPolicy? reconnect,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(clock);

        return new MqttClientFactoryContext
        {
            Address = context.Address,
            ConnectionName = string.IsNullOrWhiteSpace(connectionName)
                ? profile.Name
                : connectionName,
            Profile = profile,
            Reconnect = CopyReconnectPolicy(reconnect),
            Clock = clock
        };
    }

    private static MqttReconnectPolicy? CopyReconnectPolicy(MqttReconnectPolicy? reconnect)
        => reconnect is null
            ? null
            : reconnect with
            {
                Attributes = reconnect.Attributes is null
                    ? []
                    : new Dictionary<string, string>(reconnect.Attributes, StringComparer.Ordinal)
            };
}
