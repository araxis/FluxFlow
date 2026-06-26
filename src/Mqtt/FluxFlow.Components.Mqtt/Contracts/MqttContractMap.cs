namespace FluxFlow.Components.Mqtt.Contracts;

internal static class MqttContractMap
{
    public static IReadOnlyDictionary<string, string> Copy(
        IReadOnlyDictionary<string, string>? values)
        => values is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(values, StringComparer.Ordinal);
}
