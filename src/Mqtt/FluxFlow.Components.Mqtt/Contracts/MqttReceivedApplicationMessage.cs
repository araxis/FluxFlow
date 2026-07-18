using FluxFlow.Data;
using System.Collections.Immutable;

namespace FluxFlow.Components.Mqtt.Contracts;

public sealed record MqttReceivedApplicationMessage
{
    private IReadOnlyDictionary<string, string> _userProperties =
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);
    private IReadOnlyList<string> _matchedSubscriptions = ImmutableArray<string>.Empty;

    public required DateTimeOffset Timestamp { get; init; }

    public required string Topic { get; init; }

    public required FlowContent Content { get; init; }

    public MqttQos Qos { get; init; }

    public bool Retain { get; init; }

    public string? ResponseTopic { get; init; }

    public string? CorrelationData { get; init; }

    public IReadOnlyDictionary<string, string> UserProperties
    {
        get => _userProperties;
        init => _userProperties = value is null || value.Count == 0
            ? ImmutableDictionary.Create<string, string>(StringComparer.Ordinal)
            : value.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public IReadOnlyList<string> MatchedSubscriptions
    {
        get => _matchedSubscriptions;
        init => _matchedSubscriptions = value is null || value.Count == 0
            ? ImmutableArray<string>.Empty
            : value.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToImmutableArray();
    }
}
