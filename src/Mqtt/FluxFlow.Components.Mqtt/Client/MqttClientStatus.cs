using System.Collections.Immutable;

namespace FluxFlow.Components.Mqtt.Client;

public sealed record MqttClientStatus
{
    private IReadOnlyList<string> _desiredSubscriptions = ImmutableArray<string>.Empty;

    public required string Client { get; init; }

    public required bool IsStarted { get; init; }

    public required bool IsConnected { get; init; }

    public required bool ReconnectSuppressed { get; init; }

    public required IReadOnlyList<string> DesiredSubscriptions
    {
        get => _desiredSubscriptions;
        init => _desiredSubscriptions = value is null || value.Count == 0
            ? ImmutableArray<string>.Empty
            : value.ToImmutableArray();
    }

    public required DateTimeOffset Timestamp { get; init; }
}
