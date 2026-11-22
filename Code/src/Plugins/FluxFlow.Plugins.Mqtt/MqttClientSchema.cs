using System.Text.Json;

namespace FluxFlow.Plugins.Mqtt;

internal static class MqttClientSchema
{
    internal static PluginConnection Describe()
    {
        using var defaults = JsonDocument.Parse("""{"Type":"mqtt.client","Address":"mqtt://localhost:1883","AutoConnect":true}""");
        return new("mqtt.client", "MQTT", "Address", false, defaults.RootElement.Clone(), []) { InstanceIdentityProperty = "ClientId" };
    }
}
