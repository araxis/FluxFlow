using FluxFlow.Data;
using System.Collections.Immutable;

namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttPublishMessage
{
    private IReadOnlyDictionary<string, string> _userProperties =
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

    public required string Topic { get; init; }

    public required FlowContent Content { get; init; }

    public MqttQos Qos { get; init; }

    public bool Retain { get; init; }

    public string? ResponseTopic { get; init; }

    public string? CorrelationData { get; init; }

    public IReadOnlyDictionary<string, string> UserProperties
    {
        get => _userProperties;
        init => _userProperties = Copy(value);
    }

    private static IReadOnlyDictionary<string, string> Copy(
        IReadOnlyDictionary<string, string>? values)
        => values is null || values.Count == 0
            ? ImmutableDictionary.Create<string, string>(StringComparer.Ordinal)
            : values.ToImmutableDictionary(StringComparer.Ordinal);
}
