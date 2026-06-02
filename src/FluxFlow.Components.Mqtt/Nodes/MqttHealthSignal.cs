using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Engine.Components;

namespace FluxFlow.Components.Mqtt.Nodes;

internal static class MqttHealthSignal
{
    public static FlowDiagnosticLevel GetLevel(MqttClientHealthEvent health)
        => health.State switch
        {
            MqttClientHealthState.Faulted => FlowDiagnosticLevel.Error,
            MqttClientHealthState.Disconnected => FlowDiagnosticLevel.Warning,
            MqttClientHealthState.Reconnecting => FlowDiagnosticLevel.Warning,
            _ => FlowDiagnosticLevel.Information
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

    public static string? CreateSubject(
        MqttClientHealthEvent health,
        string? fallbackConnectionName)
        => FirstNonEmpty(health.ConnectionName, fallbackConnectionName, health.ClientId);

    public static Dictionary<string, object?> CreateDiagnosticAttributes(
        MqttClientHealthEvent health,
        string? fallbackConnectionName)
    {
        var attributes = new Dictionary<string, object?>
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

    public static Dictionary<string, string> CreateEventAttributes(
        MqttClientHealthEvent health,
        string? fallbackConnectionName)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["state"] = health.State.ToString(),
            ["timestamp"] = health.Timestamp.ToString("O")
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

    private static void AddIfPresent(
        IDictionary<string, string> attributes,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            attributes[key] = value;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
