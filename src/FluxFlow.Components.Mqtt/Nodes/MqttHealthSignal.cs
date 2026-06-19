using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Mqtt.Nodes;

internal static class MqttHealthSignal
{
    public static FlowEventLevel GetLevel(MqttClientHealthEvent health)
        => health.State switch
        {
            MqttClientHealthState.Faulted => FlowEventLevel.Error,
            MqttClientHealthState.Disconnected => FlowEventLevel.Warning,
            MqttClientHealthState.Reconnecting => FlowEventLevel.Warning,
            _ => FlowEventLevel.Information
        };

    public static string CreateMessage(MqttClientHealthEvent health)
    {
        if (!string.IsNullOrWhiteSpace(health.Message))
        {
            return health.Message;
        }

        var state = health.State.ToString();
        return string.IsNullOrWhiteSpace(health.Reason)
            ? $"MQTT connection health changed to '{state}'."
            : $"MQTT connection health changed to '{state}': {health.Reason}";
    }

    public static Dictionary<string, object?> CreateAttributes(
        MqttClientHealthEvent health,
        string? fallbackConnectionName)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["state"] = health.State.ToString(),
            ["timestamp"] = health.Timestamp
        };

        AddIfPresent(attributes, "reason", health.Reason);
        AddIfPresent(attributes, "connectionName", health.ConnectionName ?? fallbackConnectionName);
        AddIfPresent(attributes, "clientId", health.ClientId);

        foreach (var (key, value) in health.Attributes)
        {
            if (!attributes.ContainsKey(key))
            {
                attributes[key] = value;
            }
        }

        return attributes;
    }

    private static void AddIfPresent(
        IDictionary<string, object?> attributes,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            attributes[key] = value;
        }
    }
}
