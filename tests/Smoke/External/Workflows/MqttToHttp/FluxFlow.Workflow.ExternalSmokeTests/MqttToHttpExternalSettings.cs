namespace FluxFlow.Workflow.ExternalSmokeTests;

internal sealed record MqttToHttpExternalSettings(
    string Host,
    int Port,
    bool UseTls,
    string? Username,
    string? Password)
{
    private const string HostVariable = "FLUXFLOW_EXTERNAL_MQTT_HOST";
    private const string PortVariable = "FLUXFLOW_EXTERNAL_MQTT_PORT";
    private const string TlsVariable = "FLUXFLOW_EXTERNAL_MQTT_TLS";
    private const string UsernameVariable = "FLUXFLOW_EXTERNAL_MQTT_USERNAME";
    private const string PasswordVariable = "FLUXFLOW_EXTERNAL_MQTT_PASSWORD";

    internal static MqttToHttpExternalSettings Load()
    {
        var host = Environment.GetEnvironmentVariable(HostVariable);
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                $"The {HostVariable} environment variable is required when external smoke tests are enabled.");
        }

        var useTls = ParseBoolean(TlsVariable, defaultValue: false);
        var port = ParsePort(useTls ? 8883 : 1883);
        return new MqttToHttpExternalSettings(
            host.Trim(),
            port,
            useTls,
            Normalize(Environment.GetEnvironmentVariable(UsernameVariable)),
            Normalize(Environment.GetEnvironmentVariable(PasswordVariable)));
    }

    private static int ParsePort(int defaultPort)
    {
        var value = Environment.GetEnvironmentVariable(PortVariable);
        if (string.IsNullOrWhiteSpace(value))
            return defaultPort;
        if (int.TryParse(value, out var port) && port is > 0 and <= 65535)
            return port;
        throw new InvalidOperationException($"The {PortVariable} environment variable must be a valid TCP port.");
    }

    private static bool ParseBoolean(string variable, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        if (bool.TryParse(value, out var parsed))
            return parsed;
        throw new InvalidOperationException($"The {variable} environment variable must be true or false.");
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
