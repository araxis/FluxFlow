using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Timing;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Mqtt.Options;

internal static class MqttClientFactoryContexts
{
    public static MqttClientFactoryContext Create(
        RuntimeNodeFactoryContext context,
        MqttPublishOptions options,
        IMqttClock clock)
        => Create(context, options.ConnectionName, options.Connection, clock);

    public static MqttClientFactoryContext Create(
        RuntimeNodeFactoryContext context,
        MqttSubscriptionOptions options,
        IMqttClock clock)
        => Create(context, options.ConnectionName, options.Connection, clock);

    private static MqttClientFactoryContext Create(
        RuntimeNodeFactoryContext context,
        string? connectionName,
        MqttConnectionProfile profile,
        IMqttClock clock)
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
            Clock = clock
        };
    }
}
